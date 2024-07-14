using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

internal sealed class EntityFrameworkCache<TContext> : IDistributedCache
    where TContext : DbContext, ICacheDbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    private readonly Duration _expiredItemsDeletionInterval;
    private readonly Duration _defaultSlidingExpiration;
    private Instant _lastExpirationScan;

    public EntityFrameworkCache(IDbContextFactory<TContext> contextFactory, IOptions<EntityFrameworkCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            value: options.Value.ExpiredItemsDeletionInterval,
            other: EntityFrameworkCacheHelpers.ExpiredItemsDeletionIntervalMinimum);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            value: options.Value.DefaultSlidingExpiration,
            other: Duration.Zero);

        _contextFactory = contextFactory;
        _timeProvider = options.Value.TimeProvider ?? TimeProvider.System;
        _expiredItemsDeletionInterval = options.Value.ExpiredItemsDeletionInterval;
        _defaultSlidingExpiration = options.Value.DefaultSlidingExpiration;
    }

    public byte[]? Get(string key)
    {
        var task = this.GetCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return this.GetCore(key, async: true, token).AsTask();
    }

    public void Refresh(string key)
    {
        var task = this.GetCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        await this.GetCore(key, async: true, token);
    }

    public void Remove(string key)
    {
        var task = this.RemoveCore(key, async: false, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await this.RemoveCore(key, async: true, token);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        var task = this.SetCore(key, async: false, value, options, default);
        Debug.Assert(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

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

        var absoluteExpiration = EntityFrameworkCacheHelpers.GetAbsoluteExpiration(now, options);
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
