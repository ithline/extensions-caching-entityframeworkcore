using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

using Ithline.Extensions.Caching.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class EntityFrameworkCacheServiceCollectionExtensions
{
    public static IServiceCollection AddDbContextDistributedCache<TContext>(this IServiceCollection services, Action<EntityFrameworkCacheOptions>? configureOptions = null)
        where TContext : DbContext, ICacheDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<EntityFrameworkCacheOptions>();
        services.AddSingleton<IDistributedCache, EntityFrameworkCache<TContext>>();

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return services;
    }
}
