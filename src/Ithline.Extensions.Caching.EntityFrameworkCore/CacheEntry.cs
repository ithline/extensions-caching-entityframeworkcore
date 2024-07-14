using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

/// <summary>
/// Code first model used by <see cref="EntityFrameworkCache{TContext}"/>.
/// </summary>
public sealed class CacheEntry
{
    /// <summary>
    /// The entity identifier of <see cref="CacheEntry"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The serialized value of the <see cref="CacheEntry"/>.
    /// </summary>
    public required byte[] Value { get; set; }

    /// <summary>
    /// The expiration date and time of the <see cref="CacheEntry"/>.
    /// </summary>
    public required Instant ExpiresAt { get; set; }

    /// <summary>
    /// The absolute expiration date and time of the <see cref="CacheEntry"/>.
    /// </summary>
    public Instant? AbsoluteExpiration { get; set; }

    /// <summary>
    /// The sliding expiration window of the <see cref="CacheEntry"/>.
    /// </summary>
    public Duration? SlidingExpiration { get; set; }
}