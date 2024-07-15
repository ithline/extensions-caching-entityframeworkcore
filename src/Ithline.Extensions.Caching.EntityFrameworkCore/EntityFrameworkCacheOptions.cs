using Microsoft.Extensions.Options;
using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

/// <summary>
/// Configuration options for <see cref="EntityFrameworkCache{TContext}"/>.
/// </summary>
public sealed class EntityFrameworkCacheOptions : IOptions<EntityFrameworkCacheOptions>
{
    /// <summary>
    /// The periodic interval to scan and delete expired items in the cache. Default is 30 minutes.
    /// </summary>
    public Duration ExpiredItemsDeletionInterval { get; set; } = EFCacheHelpers.ExpiredItemsDeletionInterval;

    /// <summary>
    /// The default sliding expiration set for a cache entry if neither the Absolute nor SlidingExpiration has been set explicitly.
    /// By default, its 20 minutes.
    /// </summary>
    public Duration DefaultSlidingExpiration { get; set; } = EFCacheHelpers.DefaultSlidingExpiration;

    /// <summary>
    /// Gives control over the timestamps for testing purposes.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    EntityFrameworkCacheOptions IOptions<EntityFrameworkCacheOptions>.Value => this;
}
