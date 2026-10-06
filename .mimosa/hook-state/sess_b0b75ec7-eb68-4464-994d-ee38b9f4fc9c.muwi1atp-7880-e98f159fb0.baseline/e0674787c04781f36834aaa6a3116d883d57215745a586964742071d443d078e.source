using Microsoft.EntityFrameworkCore;

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>
/// 类型化实体向量集合：基于 Zvec 的 <c>Collection</c>，按实体注解映射同步与检索。
/// 通过 <c>ZvecSet&lt;TEntity&gt;.Create</c>（建集合并生成 schema）或 <c>Open</c>（打开已有集合）获取。
/// </summary>
public interface IZvecSet<TEntity> : IDisposable
    where TEntity : class
{
    /// <summary>底层 Zvec 集合（可使用全部原生能力）。</summary>
    Collection Underlying { get; }

    /// <summary>实体主键的字符串形式（zvec 主键）。</summary>
    string KeyOf(TEntity entity);

    WriteResult Upsert(TEntity entity);

    WriteResult[] UpsertRange(IEnumerable<TEntity> entities);

    WriteResult Delete(TEntity entity);

    WriteResult[] DeleteKeys(IEnumerable<string> keys);

    /// <summary>稠密向量检索（默认取第一个 float[] 向量属性，或 fieldName 指定）。</summary>
    IReadOnlyList<SearchHit> Search(float[] vector, int topk = 10, string? filter = null, string? fieldName = null);

    /// <summary>稀疏向量检索（字段须为 SparseVector 类型）。</summary>
    IReadOnlyList<SearchHit> Search(SparseVector vector, int topk = 10, string? filter = null, string? fieldName = null);

    /// <summary>检索并回查 EF 实体：向量集合拿键与得分，再从 DbContext 取实体（混合检索闭环）。</summary>
    Task<List<SearchHit<TEntity>>> FindSimilarAsync(
        DbContext context, float[] vector, int topk = 10, string? filter = null, CancellationToken cancellationToken = default);
}

/// <summary>IZvecSet 的默认实现。</summary>
public sealed class ZvecSet<TEntity> : IZvecSet<TEntity>
    where TEntity : class
{
    private readonly EntityModel<TEntity> _model = EntityModel<TEntity>.Instance;

    public Collection Underlying { get; }

    private ZvecSet(Collection collection) => Underlying = collection;

    /// <summary>创建并打开实体向量集合（按实体注解生成 schema；可配置向量索引）。</summary>
    public static ZvecSet<TEntity> Create(string path, Action<ZvecSetOptions<TEntity>>? configure = null)
    {
        var options = new ZvecSetOptions<TEntity>();
        configure?.Invoke(options);
        EnsureEngineInitialized();

        CollectionSchema schema = EntityModel<TEntity>.Instance.BuildSchema(options.VectorIndexes);
        return new ZvecSet<TEntity>(global::Zvec.NET.Zvec.CreateAndOpen(path, schema, options.CollectionOption));
    }

    /// <summary>打开已有实体向量集合。</summary>
    public static ZvecSet<TEntity> Open(string path, CollectionOption? option = null)
    {
        EnsureEngineInitialized();
        return new ZvecSet<TEntity>(global::Zvec.NET.Zvec.Open(path, option));
    }

    private static void EnsureEngineInitialized()
    {
        if (!global::Zvec.NET.Zvec.IsInitialized)
        {
            global::Zvec.NET.Zvec.Init();
        }
    }

    public string KeyOf(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _model.KeyGetter(entity);
    }

    public WriteResult Upsert(TEntity entity) => Underlying.Upsert(BuildDoc(entity));

    public WriteResult[] UpsertRange(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return Underlying.Upsert(entities.Select(BuildDoc));
    }

    public WriteResult Delete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Underlying.Delete(KeyOf(entity));
    }

    public WriteResult[] DeleteKeys(IEnumerable<string> keys) => Underlying.Delete(keys);

    public IReadOnlyList<SearchHit> Search(float[] vector, int topk = 10, string? filter = null, string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        IReadOnlyList<Doc> docs = Underlying.Query(
            new Query(ResolveVectorField(fieldName, DataType.VectorFp32), vector: vector), topk, filter);
        return [.. docs.Select(ToHit)];
    }

    public IReadOnlyList<SearchHit> Search(SparseVector vector, int topk = 10, string? filter = null, string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(vector);
        IReadOnlyList<Doc> docs = Underlying.Query(
            new Query(ResolveVectorField(fieldName, DataType.SparseVectorFp32), vector: vector), topk, filter);
        return [.. docs.Select(ToHit)];
    }

    public async Task<List<SearchHit<TEntity>>> FindSimilarAsync(
        DbContext context, float[] vector, int topk = 10, string? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<SearchHit> hits = Search(vector, topk, filter);
        if (hits.Count == 0)
        {
            return [];
        }

        // 键在向量集合与 EF 实体间以字符串对齐（数值/Guid 键用不变文化格式化）。
        List<string> ids = [.. hits.Select(h => h.Id)];
        var predicate = _model.BuildKeyInExpression(ids);
        Dictionary<string, TEntity> entities = await context.Set<TEntity>()
            .Where(predicate)
            .ToDictionaryAsync(_model.KeyGetter, StringComparer.Ordinal, cancellationToken);

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

            throw new ArgumentException($"属性 {fieldName} 不是 {typeof(TEntity).Name} 的向量属性。");
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

    public void Dispose() => Underlying.Dispose();
}
