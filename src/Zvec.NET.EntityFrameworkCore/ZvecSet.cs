using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

// CA1000（泛型类型上不宜声明静态成员）：Create/Open 为按实体类型的工厂方法，
// 静态工厂是刻意设计（约束 TEntity 并返回封闭类型），不采用独立非泛型工厂类。
#pragma warning disable CA1000

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>实体类型对裁剪器的成员保留需求：属性映射（反射 schema）+ EF Core Set&lt;T&gt; 的要求集合。</summary>
internal static class VectorTrimming
{
    public const DynamicallyAccessedMemberTypes EntityMembers =
        DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;
}

/// <summary>
/// 类型化实体向量集合：基于 Zvec 的 <c>Collection</c>，按实体注解映射同步与检索。
/// 通过 <c>ZvecSet&lt;TEntity&gt;.Create</c>（建集合并生成 schema）或 <c>Open</c>（打开已有集合）获取。
/// </summary>
public interface IZvecSet<[DynamicallyAccessedMembers(VectorTrimming.EntityMembers)] TEntity> : IDisposable
    where TEntity : class
{
    /// <summary>底层 Zvec 集合（可使用全部原生能力）。</summary>
    Collection Underlying { get; }

    /// <summary>实体主键的字符串形式（zvec 主键）。</summary>
    /// <param name="entity">实体实例。</param>
    string KeyOf(TEntity entity);

    /// <summary>Upsert 单个实体到向量集合。</summary>
    /// <param name="entity">实体实例。</param>
    WriteResult Upsert(TEntity entity);

    /// <summary>Upsert 一批实体到向量集合。</summary>
    /// <param name="entities">实体集合。</param>
    WriteResult[] UpsertRange(IEnumerable<TEntity> entities);

    /// <summary>异步 Upsert 单个实体到向量集合。</summary>
    /// <param name="entity">实体实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WriteResult> UpsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>异步 Upsert 一批实体到向量集合。</summary>
    /// <param name="entities">实体集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WriteResult[]> UpsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>按实体主键删除对应文档。</summary>
    /// <param name="entity">实体实例。</param>
    WriteResult Delete(TEntity entity);

    /// <summary>按主键字符串删除对应文档。</summary>
    /// <param name="keys">主键字符串集合。</param>
    WriteResult[] DeleteKeys(IEnumerable<string> keys);

    /// <summary>稠密向量检索（默认取第一个 float[] 向量属性，或 fieldName 指定）。</summary>
    /// <param name="vector">查询向量。</param>
    /// <param name="topk">返回条数。</param>
    /// <param name="filter">引擎端布尔过滤表达式（可为 null）。</param>
    /// <param name="fieldName">向量属性名（null = 首个 FP32 向量属性）。</param>
    IReadOnlyList<SearchHit> Search(float[] vector, int topk = 10, string? filter = null, string? fieldName = null);

    /// <summary>稀疏向量检索（字段须为 SparseVector 类型）。</summary>
    /// <param name="vector">稀疏查询向量。</param>
    /// <param name="topk">返回条数。</param>
    /// <param name="filter">引擎端布尔过滤表达式（可为 null）。</param>
    /// <param name="fieldName">向量属性名（null = 首个稀疏向量属性）。</param>
    IReadOnlyList<SearchHit> Search(SparseVector vector, int topk = 10, string? filter = null, string? fieldName = null);

    /// <summary>检索并回查 EF 实体：向量集合拿键与得分，再从 DbContext 取实体（混合检索闭环）。</summary>
    /// <param name="context">EF Core DbContext。</param>
    /// <param name="vector">查询向量。</param>
    /// <param name="topk">返回条数。</param>
    /// <param name="filter">引擎端布尔过滤表达式（可为 null）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<List<SearchHit<TEntity>>> FindSimilarAsync(
        DbContext context, float[] vector, int topk = 10, string? filter = null, CancellationToken cancellationToken = default);
}

/// <summary>IZvecSet 的默认实现。</summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
public sealed class ZvecSet<[DynamicallyAccessedMembers(VectorTrimming.EntityMembers)] TEntity> : IZvecSet<TEntity>
    where TEntity : class
{
    private readonly EntityModel<TEntity> _model = EntityModel<TEntity>.Instance;

    /// <inheritdoc/>
    public Collection Underlying { get; }

    private ZvecSet(Collection collection) => Underlying = collection;

    /// <summary>创建并打开实体向量集合（按实体注解生成 schema；可配置向量索引）。</summary>
    /// <param name="path">集合目录（须不存在）。</param>
    /// <param name="configure">集合与索引配置（可选）。</param>
    public static ZvecSet<TEntity> Create(string path, Action<ZvecSetOptions<TEntity>>? configure = null)
    {
        var options = new ZvecSetOptions<TEntity>();
        configure?.Invoke(options);
        ZvecEngine.EnsureInitialized();

        CollectionSchema schema = EntityModel<TEntity>.Instance.BuildSchema(options.VectorIndexes);
        return new ZvecSet<TEntity>(global::Zvec.NET.Zvec.CreateAndOpen(path, schema, options.CollectionOption));
    }

    /// <summary>打开已有实体向量集合。</summary>
    /// <param name="path">集合目录。</param>
    /// <param name="option">打开选项（只读/mmap 等，可选）。</param>
    public static ZvecSet<TEntity> Open(string path, CollectionOption? option = null)
    {
        ZvecEngine.EnsureInitialized();
        return new ZvecSet<TEntity>(global::Zvec.NET.Zvec.Open(path, option));
    }

    /// <inheritdoc/>
    public string KeyOf(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _model.KeyGetter(entity);
    }

    /// <inheritdoc/>
    public WriteResult Upsert(TEntity entity) => Underlying.Upsert(BuildDoc(entity));

    /// <inheritdoc/>
    public WriteResult[] UpsertRange(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return Underlying.Upsert(entities.Select(BuildDoc));
    }

    /// <inheritdoc/>
    public Task<WriteResult> UpsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Underlying.UpsertAsync(BuildDoc(entity), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<WriteResult[]> UpsertRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return Underlying.UpsertAsync(entities.Select(BuildDoc), cancellationToken);
    }

    /// <inheritdoc/>
    public WriteResult Delete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Underlying.Delete(KeyOf(entity));
    }

    /// <inheritdoc/>
    public WriteResult[] DeleteKeys(IEnumerable<string> keys) => Underlying.Delete(keys);

    /// <summary>
    /// 过滤表达式边界校验（分层防御；核心包 Collection.Query 亦会校验）：
    /// null 放行（不过滤），空串/含 NUL 拒绝。表达式由 Zvec 引擎端解析（对齐 Python SDK
    /// 的非参数化 filter 参数），来源不可信时调用方须自行校验。
    /// </summary>
    private static string? ValidateFilter(string? filter) => filter is null
        ? null
        : filter.Length == 0 || filter.Contains('\0')
            ? throw new ArgumentException("过滤表达式不能为空串或包含 NUL 字符。", nameof(filter))
            : filter;

    /// <inheritdoc/>
    public IReadOnlyList<SearchHit> Search(float[] vector, int topk = 10, string? filter = null, string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        IReadOnlyList<Doc> docs = Underlying.Query(
            new Query(ResolveVectorField(fieldName, DataType.VectorFp32), vector: vector), topk, ValidateFilter(filter));
        return [.. docs.Select(ToHit)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<SearchHit> Search(SparseVector vector, int topk = 10, string? filter = null, string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        IReadOnlyList<Doc> docs = Underlying.Query(
            new Query(ResolveVectorField(fieldName, DataType.SparseVectorFp32), vector: vector), topk, ValidateFilter(filter));
        return [.. docs.Select(ToHit)];
    }

    /// <inheritdoc/>
    public async Task<List<SearchHit<TEntity>>> FindSimilarAsync(
        DbContext context, float[] vector, int topk = 10, string? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SearchHit> hits = Search(vector, topk, filter);
        if (hits.Count == 0)
        {
            return [];
        }

        // 键在向量集合与 EF 实体间以字符串对齐（写入侧 KeyGetter 与本谓词共用同一表达式）。
        List<string> ids = [.. hits.Select(h => h.Id)];
        var predicate = _model.BuildKeyInExpression(ids);
        Dictionary<string, TEntity> entities = await context.Set<TEntity>()
            .Where(predicate)
            .ToDictionaryAsync(_model.KeyGetter, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var result = new List<SearchHit<TEntity>>(hits.Count);
        foreach (SearchHit hit in hits)
        {
            if (entities.TryGetValue(hit.Id, out TEntity? entity))
            {
                result.Add(new SearchHit<TEntity>(entity, hit.Score));
            }
        }

        return result;
    }

    private Doc BuildDoc(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _model.ToDoc(entity);
    }

    private static SearchHit ToHit(Doc doc) => new(doc.Id, doc.Score ?? 0f, doc);

    private string ResolveVectorField(string? fieldName, DataType expected)
    {
        if (fieldName is not null)
        {
            foreach (EntityModel<TEntity>.PropertyMapping vector in _model.Vectors)
            {
                if (vector.Property.Name == fieldName)
                {
                    return fieldName;
                }
            }

            throw new ArgumentException($"属性 {fieldName} 不是 {typeof(TEntity).Name} 的向量属性。", nameof(fieldName));
        }

        foreach (EntityModel<TEntity>.PropertyMapping vector in _model.Vectors)
        {
            if (vector.DataType == expected)
            {
                return vector.Property.Name;
            }
        }

        throw new ArgumentException($"{typeof(TEntity).Name} 没有 {expected} 类型的向量属性，请通过 fieldName 指定。");
    }

    /// <inheritdoc/>
    public void Dispose() => Underlying.Dispose();
}

#pragma warning restore CA1000
