using System.Diagnostics.CodeAnalysis;
using Ithline.Extensions.Caching.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up EF Core distributed cache related services in an <see cref="IServiceCollection" />.
/// </summary>
public static class EntityFrameworkCacheServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="DbContext" /> distributed caching services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <typeparam name="TContext">The type of the <see cref="DbContext" />.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="configureOptions">
    /// An <see cref="Action{EntityFrameworkCacheOptions}" /> to configure the provided
    /// <see cref="EntityFrameworkCacheOptions" />.
    /// </param>
    /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
    public static IServiceCollection AddDbContextDistributedCache<[DynamicallyAccessedMembers(EFCacheHelpers.MemberTypes)] TContext>(
        this IServiceCollection services,
        Action<EntityFrameworkCacheOptions>? configureOptions = null)
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
