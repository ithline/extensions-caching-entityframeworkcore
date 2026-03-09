using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;

namespace Ithline.Extensions.Caching.EntityFrameworkCore;

internal static class EFCacheHelpers
{
    internal const DynamicallyAccessedMemberTypes MemberTypes =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties;

    public static readonly TimeSpan ExpiredItemsDeletionInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ExpiredItemsDeletionIntervalMinimum = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(20);

    public static DateTimeOffset? GetAbsoluteExpiration(DateTimeOffset now, DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpirationRelativeToNow is TimeSpan relativeToNow)
        {
            return now.Add(relativeToNow);
        }

        return options.AbsoluteExpiration;
    }
}
