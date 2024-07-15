using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

/// <summary>
/// Distributed cache implementation using <see cref="DbContext"/>.
/// </summary>
/// <typeparam name="TContext">The type of the <see cref="DbContext"/>.</typeparam>
public sealed class EntityFrameworkCache<[DynamicallyAccessedMembers(EFCacheHelpers.MemberTypes)] TContext> : IDistributedCache
    where TContext : DbContext, ICacheDbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    private readonly Duration _expiredItemsDeletionInterval;
    private readonly Duration _defaultSlidingExpiration;
    private Instant _lastExpirationScan;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCache{TContext}"/>.
    /// </summary>
    /// <param name="contextFactory">Factory used to create instances of <typeparamref name="TContext"/>.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="options">Options used to configure behavior of the cache.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="contextFactory"/> is <see langword="null"/>.-or-
    /// <paramref name="logger"/> is <see langword="null"/>.-or-
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="EntityFrameworkCacheOptions.ExpiredItemsDeletionInterval"/> is less than 5 minutes.-or-
    /// <see cref="EntityFrameworkCacheOptions.DefaultSlidingExpiration"/> is less than or equal to 0.
    /// </exception>
    public EntityFrameworkCache(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EntityFrameworkCache<TContext>> logger,
        IOptions<EntityFrameworkCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            value: options.Value.ExpiredItemsDeletionInterval,
            other: EFCacheHelpers.ExpiredItemsDeletionIntervalMinimum);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            value: options.Value.DefaultSlidingExpiration,
            other: Duration.Zero);

        _contextFactory = contextFactory;
        _timeProvider = options.Value.TimeProvider ?? TimeProvider.System;
        _expiredItemsDeletionInterval = options.Value.ExpiredItemsDeletionInterval;
        _defaultSlidingExpiration = options.Value.DefaultSlidingExpiration;
    }

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        var task = this.GetCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return this.GetCore(key, async: true, token).AsTask();
    }

    /// <inheritdoc />
    public void Refresh(string key)
    {
        var task = this.GetCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        await this.GetCore(key, async: true, token);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        var task = this.RemoveCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await this.RemoveCore(key, async: true, token);
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var task = this.SetCore(key, async: false, value, options, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        return this.SetCore(key, async: true, value, options, token).AsTask();
    }

    private async ValueTask<byte[]?> GetCore(string key, bool async, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetCurrentInstant();

        using var context = _contextFactory.CreateDbContext();
        var query = context.CacheEntries
            .TagWith("EFCache.Get.Find")
            .Where(t => t.Id == key)
            .Where(t => now <= t.ExpiresAt);

        var entry = async
            ? await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : query.FirstOrDefault();

        // ak sme našli záznam v cache a máme sliding expiration a expiration nie je nastavená na absolute expiration, tak aktualizujeme expiresAt
        if (entry is not null && entry.SlidingExpiration is Duration slidingExpiration && entry.AbsoluteExpiration != entry.ExpiresAt)
        {
            entry.ExpiresAt = entry.AbsoluteExpiration is Instant absoluteExpiration && (now - absoluteExpiration) <= slidingExpiration
                ? absoluteExpiration
                : now + slidingExpiration;

            if (async)
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context.SaveChanges();
            }
        }

        await this.ScanExpiredItemsCore(context, async, cancellationToken).ConfigureAwait(false);

        return entry?.Value;
    }

    private async ValueTask SetCore(string key, bool async, byte[] value, DistributedCacheEntryOptions options, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetCurrentInstant();

        Duration? slidingExpiration = options.SlidingExpiration is TimeSpan ts
            ? Duration.FromTimeSpan(ts)
            : null;

        var absoluteExpiration = EFCacheHelpers.GetAbsoluteExpiration(now, options);
        var expiresAt = absoluteExpiration ?? now + (slidingExpiration ?? _defaultSlidingExpiration);

        using var context = _contextFactory.CreateDbContext();
        var query = context.CacheEntries
            .TagWith("EFCache.Set.Find")
            .Where(t => t.Id == key);

        var entry = async
            ? await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : query.FirstOrDefault();

        if (entry is null)
        {
            context.CacheEntries.Add(entry = new CacheEntry
            {
                Id = key,
                Value = value,
                ExpiresAt = expiresAt,
                SlidingExpiration = slidingExpiration,
                AbsoluteExpiration = absoluteExpiration,
            });
        }
        else
        {
            entry.Value = value;
            entry.ExpiresAt = expiresAt;
            entry.SlidingExpiration = slidingExpiration;
            entry.AbsoluteExpiration = absoluteExpiration;
        }

        if (async)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            context.SaveChanges();
        }

        await this.ScanExpiredItemsCore(context, async, cancellationToken);
    }

    private async ValueTask RemoveCore(string key, bool async, CancellationToken cancellationToken)
    {
        using var context = _contextFactory.CreateDbContext();
        var query = context.CacheEntries
            .TagWith("EFCache.Remove")
            .Where(t => t.Id == key);

        var rowCount = async
            ? await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false)
            : query.ExecuteDelete();

        await this.ScanExpiredItemsCore(context, async, cancellationToken);
    }

    private async ValueTask ScanExpiredItemsCore(TContext context, bool async, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetCurrentInstant();
        if (now - _lastExpirationScan <= _expiredItemsDeletionInterval)
        {
            return;
        }

        _lastExpirationScan = now;

        var query = context.CacheEntries
            .TagWith("EFCache.Scan")
            .Where(t => t.ExpiresAt < now);

        var rowCount = async
            ? await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false)
            : query.ExecuteDelete();
    }
}
