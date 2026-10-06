using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>
/// 实体类型 ↔ Zvec 字段的反射映射模型（每实体类型缓存一份）。
/// 键：<see cref="VectorKeyAttribute"/> 或约定 "Id"/"&lt;实体名&gt;Id"；
/// 向量：<see cref="VectorFieldAttribute"/>；标量按类型自动映射；<see cref="VectorIgnoredAttribute"/> 排除。
/// </summary>
internal sealed class EntityModel<TEntity> where TEntity : class
{
    private static readonly ConcurrentDictionary<Type, object> Cache = [];

    internal static EntityModel<TEntity> Instance => (EntityModel<TEntity>)Cache.GetOrAdd(
        typeof(TEntity), _ => new EntityModel<TEntity>());

    public string CollectionName { get; }

    public Func<TEntity, string> KeyGetter { get; }

    /// <summary>键的字符串化表达式（供 EF 查询翻译）。</summary>
    public Expression<Func<TEntity, string>> KeyExpression { get; }

    /// <summary>构建 e => ids.Contains(key(e)) 的谓词（EF 可翻译为 IN）。</summary>
    public Expression<Func<TEntity, bool>> BuildKeyInExpression(List<string> ids)
    {
        MethodInfo contains = typeof(List<string>).GetMethod(nameof(List<string>.Contains), [typeof(string)])!;
        Expression body = Expression.Call(Expression.Constant(ids), contains, KeyExpression.Body);
        return Expression.Lambda<Func<TEntity, bool>>(body, KeyExpression.Parameters[0]);
    }

    public PropertyMapping Key { get; }

    public IReadOnlyList<PropertyMapping> Scalars { get; }

    public IReadOnlyList<PropertyMapping> Vectors { get; }

    private EntityModel()
    {
        Type type = typeof(TEntity);
        CollectionName = type.GetCustomAttribute<VectorCollectionAttribute>()?.Name ?? type.Name;

        List<PropertyMapping> scalars = [];
        List<PropertyMapping> vectors = [];
        PropertyMapping? key = null;

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<VectorIgnoredAttribute>() is not null || !property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.GetCustomAttribute<VectorKeyAttribute>() is not null)
            {
                key ??= new PropertyMapping(property, MapKeyProperty(property));
                continue;
            }

            if (property.GetCustomAttribute<VectorFieldAttribute>() is { } vectorAttr)
            {
                vectors.Add(new PropertyMapping(property, MapVectorProperty(property, vectorAttr)));
                continue;
            }

            if (IsKeyByConvention(property))
            {
                key ??= new PropertyMapping(property, MapKeyProperty(property));
                continue;
            }

            DataType? scalarType = MapScalarProperty(property);
            if (scalarType.HasValue)
            {
                scalars.Add(new PropertyMapping(property, scalarType.Value));
            }
            // 其余类型静默忽略（复杂对象不参与同步）。
        }

        Key = key ?? throw new InvalidOperationException(
            $"实体 {type.Name} 缺少主键：请用 {nameof(VectorKeyAttribute)} 标注或提供 Id 属性。");
        if (vectors.Count == 0)
        {
            throw new InvalidOperationException(
                $"实体 {type.Name} 没有向量属性：请用 {nameof(VectorFieldAttribute)} 标注至少一个向量字段。");
        }

        Scalars = scalars;
        Vectors = vectors;

        KeyExpression = BuildKeyExpression();
        KeyGetter = KeyExpression.Compile();
    }

    private bool IsKeyByConvention(PropertyInfo property) =>
        property.Name == "Id" || property.Name == typeof(TEntity).Name + "Id";

    private static readonly HashSet<Type> SupportedKeyTypes =
    [
        typeof(string), typeof(int), typeof(long), typeof(Guid),
    ];

    private static DataType MapKeyProperty(PropertyInfo property)
    {
        if (!SupportedKeyTypes.Contains(property.PropertyType))
        {
            throw new NotSupportedException(
                $"主键 {property.Name} 的类型 {property.PropertyType.Name} 不受支持（支持 string/int/long/Guid）。");
        }

        // 键统一以字符串形式作为 zvec 主键存储。
        return DataType.String;
    }

    private static DataType MapVectorProperty(PropertyInfo property, VectorFieldAttribute attribute)
    {
        if (property.PropertyType == typeof(float[]) && attribute.Dimension > 0)
        {
            return DataType.VectorFp32;
        }

        if (property.PropertyType == typeof(SparseVector))
        {
            return DataType.SparseVectorFp32;
        }

        throw new NotSupportedException(
            $"向量属性 {property.Name} 需要 float[]（并指定 Dimension）或 SparseVector 类型。");
    }

    private static DataType? MapScalarProperty(PropertyInfo property) => property.PropertyType == typeof(string) ? DataType.String
        : property.PropertyType == typeof(int) || property.PropertyType == typeof(int?) ? DataType.Int32
        : property.PropertyType == typeof(long) || property.PropertyType == typeof(long?) ? DataType.Int64
        : property.PropertyType == typeof(float) || property.PropertyType == typeof(float?) ? DataType.Float
        : property.PropertyType == typeof(double) || property.PropertyType == typeof(double?) ? DataType.Double
        : property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?) ? DataType.Bool
        : property.PropertyType == typeof(string[]) ? DataType.ArrayString
        : property.PropertyType == typeof(int[]) ? DataType.ArrayInt32
        : property.PropertyType == typeof(long[]) ? DataType.ArrayInt64
        : property.PropertyType == typeof(float[]) ? null // 有歧义的 float[] 未标注则忽略，避免误当向量。
        : property.PropertyType == typeof(double[]) ? DataType.ArrayDouble
        : null;

    public int DimensionOf(PropertyMapping vector) => vector.Property.GetCustomAttribute<VectorFieldAttribute>()?.Dimension ?? 0;

    private Expression<Func<TEntity, string>> BuildKeyExpression()
    {
        // string 键直接取属性；数值/Guid 键调用实例 ToString()（EF 关系库可翻译为 CAST/CONVERT）。
        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression body = Expression.Property(parameter, Key.Property);
        if (Key.Property.PropertyType != typeof(string))
        {
            MethodInfo toString = Key.Property.PropertyType.GetMethod(nameof(ToString), Type.EmptyTypes)!;
            body = Expression.Call(body, toString);
        }

        return Expression.Lambda<Func<TEntity, string>>(body, parameter);
    }

    internal Doc ToDoc(TEntity entity)
    {
        var doc = new Doc(KeyGetter(entity));
        foreach (PropertyMapping scalar in Scalars)
        {
            doc.Fields[scalar.Property.Name] = scalar.Property.GetValue(entity);
        }

        foreach (PropertyMapping vector in Vectors)
        {
            object? value = vector.Property.GetValue(entity);
            if (value is not null)
            {
                doc.Vectors[vector.Property.Name] = value;
            }
        }

        return doc;
    }

    internal CollectionSchema BuildSchema(IReadOnlyDictionary<string, IndexParam>? vectorIndexes)
    {
        var schema = new CollectionSchema(CollectionName);
        foreach (PropertyMapping scalar in Scalars)
        {
            schema.AddField(new FieldSchema(scalar.Property.Name, scalar.DataType,
                nullable: IsNullable(scalar.Property)));
        }

        foreach (PropertyMapping vector in Vectors)
        {
            schema.AddVector(new VectorSchema(vector.Property.Name, vector.DataType,
                (uint)DimensionOf(vector), nullable: IsNullable(vector.Property),
                indexParam: vectorIndexes?.GetValueOrDefault(vector.Property.Name)));
        }

        return schema;
    }

    private static bool IsNullable(PropertyInfo property) =>
        NullabilityInfoContextCache.For(property) == NullabilityState.Nullable;

    private static class NullabilityInfoContextCache
    {
        [ThreadStatic]
        private static NullabilityInfoContext? _context;

        public static NullabilityState For(PropertyInfo property)
        {
            _context ??= new NullabilityInfoContext();
            return _context.Create(property).ReadState;
        }
    }

    internal readonly record struct PropertyMapping(PropertyInfo Property, DataType DataType);
}
