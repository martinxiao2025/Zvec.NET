using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>ZvecSet 创建选项：向量索引配置与集合打开选项。</summary>
public sealed class ZvecSetOptions<[DynamicallyAccessedMembers(VectorTrimming.EntityMembers)] TEntity>
    where TEntity : class
{
    private readonly Dictionary<string, IndexParam> _vectorIndexes = [];

    internal IReadOnlyDictionary<string, IndexParam> VectorIndexes => _vectorIndexes;

    /// <summary>集合打开选项（只读 / mmap 等）。</summary>
    public CollectionOption CollectionOption { get; set; } = new();

    /// <summary>为向量属性配置索引（默认不建索引，由引擎暴力检索）。</summary>
    public ZvecSetOptions<TEntity> WithVectorIndex(Expression<Func<TEntity, object?>> vectorProperty, IndexParam indexParam)
    {
        ArgumentNullException.ThrowIfNull(vectorProperty);
        ArgumentNullException.ThrowIfNull(indexParam);

        string name = PropertyName(vectorProperty);
        if (EntityModel<TEntity>.Instance.Vectors.All(v => v.Property.Name != name))
        {
            throw new ArgumentException($"属性 {name} 不是 {typeof(TEntity).Name} 的向量属性（需 [VectorField] 标注）。");
        }

        _vectorIndexes[name] = indexParam;
        return this;
    }

    private static string PropertyName(Expression<Func<TEntity, object?>> expression) => expression.Body switch
    {
        MemberExpression member => member.Member.Name,
        UnaryExpression { Operand: MemberExpression inner } => inner.Member.Name,
        _ => throw new ArgumentException("表达式必须是属性访问（e => e.Property）。", nameof(expression)),
    };
}
