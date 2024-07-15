using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;
using NodaTime;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

internal static class EFCacheHelpers
{
    internal const DynamicallyAccessedMemberTypes MemberTypes =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties;

    public static readonly Duration ExpiredItemsDeletionInterval = Duration.FromMinutes(30);
    public static readonly Duration ExpiredItemsDeletionIntervalMinimum = Duration.FromMinutes(5);

    public static readonly Duration DefaultSlidingExpiration = Duration.FromMinutes(20);

    public static Instant? GetAbsoluteExpiration(Instant now, DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpirationRelativeToNow is TimeSpan relativeToNow)
        {
            return now + Duration.FromTimeSpan(relativeToNow);
        }

        if (options.AbsoluteExpiration is not DateTimeOffset absoluteExpiration)
        {
            return null;
        }

        var instant = Instant.FromDateTimeOffset(absoluteExpiration);
        if (instant <= now)
        {
            throw new InvalidOperationException("The absolute expiration value must be in the future.");
        }

        return instant;

    }

    public static Instant GetCurrentInstant(this TimeProvider timeProvider)
    {
        return Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
    }
}
