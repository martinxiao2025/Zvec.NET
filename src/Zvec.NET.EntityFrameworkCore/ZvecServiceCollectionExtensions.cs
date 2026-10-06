using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>DI 注册扩展：把实体向量集合并入 ASP.NET Core / Generic Host 服务集合。</summary>
public static class ZvecServiceCollectionExtensions
{
    /// <summary>
    /// 注册实体向量集合（单例）。引擎按需自动初始化（可用 <paramref name="configureEngine"/> 定制日志/线程/内存）。
    /// </summary>
    public static IServiceCollection AddZvecSets(this IServiceCollection services,
        Action<ZvecOptions>? configureEngine = null,
        Action<ZvecSetRegistrationBuilder>? configureSets = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(_ =>
        {
            if (!global::Zvec.NET.Zvec.IsInitialized)
            {
                ZvecOptions options = new();
                configureEngine?.Invoke(options);
                global::Zvec.NET.Zvec.Init(options);
            }

            return new ZvecEngineMarker();
        });

        var builder = new ZvecSetRegistrationBuilder(services);
        configureSets?.Invoke(builder);
        return services;
    }

    /// <summary>占位单例：确保引擎在任何集合解析前完成初始化。</summary>
    public sealed class ZvecEngineMarker
    {
        internal ZvecEngineMarker()
        {
        }
    }

    public sealed class ZvecSetRegistrationBuilder(IServiceCollection services)
    {
        internal IServiceCollection Services { get; } = services;

        /// <summary>注册由工厂创建的实体向量集合（单例 IZvecSet&lt;TEntity&gt; + ZvecSet&lt;TEntity&gt;）。</summary>
        public ZvecSetRegistrationBuilder AddSet<TEntity>(
            string path,
            Action<ZvecSetOptions<TEntity>>? configure = null)
            where TEntity : class
        {
            Services.TryAddSingleton<ZvecSet<TEntity>>(sp =>
            {
                _ = sp.GetRequiredService<ZvecEngineMarker>();
                return ZvecSet<TEntity>.Create(path, configure);
            });
            Services.TryAddSingleton<IZvecSet<TEntity>>(sp => sp.GetRequiredService<ZvecSet<TEntity>>());
            return this;
        }

        /// <summary>注册打开已有集合（只读或读写由 options 决定）。</summary>
        public ZvecSetRegistrationBuilder AddExistingSet<TEntity>(
            string path, CollectionOption? option = null)
            where TEntity : class
        {
            Services.TryAddSingleton<ZvecSet<TEntity>>(sp =>
            {
                _ = sp.GetRequiredService<ZvecEngineMarker>();
                return ZvecSet<TEntity>.Open(path, option);
            });
            Services.TryAddSingleton<IZvecSet<TEntity>>(sp => sp.GetRequiredService<ZvecSet<TEntity>>());
            return this;
        }
    }
}
