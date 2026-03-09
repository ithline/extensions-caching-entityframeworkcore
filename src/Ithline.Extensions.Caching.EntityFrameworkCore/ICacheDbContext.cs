using Microsoft.EntityFrameworkCore;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

/// <summary>
/// Interface used to store instances of <see cref="CacheEntry"/> in a <see cref="DbContext"/>
/// </summary>
public interface ICacheDbContext
{
    /// <summary>
    /// A collection of <see cref="CacheEntry"/>
    /// </summary>
    public DbSet<CacheEntry> CacheEntries { get; }
}
