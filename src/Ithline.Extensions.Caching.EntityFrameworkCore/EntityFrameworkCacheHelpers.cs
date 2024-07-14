using Microsoft.Extensions.Caching.Distributed;
using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

internal static class EntityFrameworkCacheHelpers
{
    public static readonly Duration ExpiredItemsDeletionInterval = Duration.FromMinutes(30);
    public static readonly Duration ExpiredItemsDeletionIntervalMinimum = Duration.FromMinutes(5);

    public static readonly Duration DefaultSlidingExpiration = Duration.FromMinutes(20);

    public static Instant? GetAbsoluteExpiration(Instant now, DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpirationRelativeToNow is TimeSpan relativeToNow)
        {
            return now + Duration.FromTimeSpan(relativeToNow);
        }

        if (options.AbsoluteExpiration is DateTimeOffset absoluteExpiration)
        {
            var instant = Instant.FromDateTimeOffset(absoluteExpiration);
            if (instant <= now)
            {
                throw new InvalidOperationException("The absolute expiration value must be in the future.");
            }

            return instant;
        }

        return null;
    }
    
    public static Instant GetCurrentInstant(this TimeProvider timeProvider)
    {
        return Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
    }
}
