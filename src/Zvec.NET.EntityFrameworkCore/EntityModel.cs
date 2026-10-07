using System.Linq.Expressions;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Zvec.NET.EntityFrameworkCore;

/// <summary>
/// 实体类型 ↔ Zvec 字段的反射映射模型（每实体类型缓存一份）。
/// 键：<see cref="VectorKeyAttribute"/>（显式标注优先）或约定 "Id"/"&lt;实体名&gt;Id"；
/// 向量：<see cref="VectorFieldAttribute"/>；标量按类型自动映射；<see cref="VectorIgnoredAttribute"/> 排除。
/// </summary>
internal sealed class EntityModel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity>
    where TEntity : class
{
    private static EntityModel<TEntity>? _instance;

    /// <summary>惰性单例；构造异常不缓存（下次访问重试，错误语义稳定），并发首触达收敛到单实例。</summary>
    internal static EntityModel<TEntity> Instance => _instance ?? CreateCached();

    private static EntityModel<TEntity> CreateCached()
    {
        var created = new EntityModel<TEntity>();
        Interlocked.CompareExchange(ref _instance, created, null);
        return Volatile.Read(ref _instance)!;
    }

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
        PropertyMapping? attributeKey = null;
        PropertyMapping? conventionKey = null;
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!seenNames.Add(property.Name))
            {
                throw new InvalidOperationException(
                    $"实体 {type.Name} 存在同名属性 {property.Name}（new 隐藏继承成员），无法建立确定映射。");
            }

            if (property.GetCustomAttribute<VectorIgnoredAttribute>() is not null || !property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.GetCustomAttribute<VectorKeyAttribute>() is not null)
            {
                if (attributeKey.HasValue)
                {
                    throw new InvalidOperationException(
                        $"实体 {type.Name} 标注了多个 [VectorKey]（{attributeKey.Value.Property.Name} 与 {property.Name}），只能有一个主键。");
                }

                attributeKey = new PropertyMapping(property, MapKeyProperty(property), 0);
                continue;
            }

            if (property.GetCustomAttribute<VectorFieldAttribute>() is { } vectorAttr)
            {
                vectors.Add(new PropertyMapping(property, MapVectorProperty(property, vectorAttr), vectorAttr.Dimension));
                continue;
            }

            if (IsKeyByConvention(property))
            {
                if (conventionKey.HasValue)
                {
                    throw new InvalidOperationException(
                        $"实体 {type.Name} 同时存在约定键 {conventionKey.Value.Property.Name} 与 {property.Name}，请用 [VectorKey] 显式指定。");
                }

                conventionKey = new PropertyMapping(property, MapKeyProperty(property), 0);
                continue;
            }

            DataType? scalarType = MapScalarProperty(property);
            if (scalarType.HasValue)
            {
                scalars.Add(new PropertyMapping(property, scalarType.Value, 0));
            }
            // 其余类型静默忽略（复杂对象不参与同步）。
        }

        // 显式 [VectorKey] 优先于命名约定，避免约定键静默吞掉显式标注。
        Key = attributeKey ?? conventionKey ?? throw new InvalidOperationException(
            $"实体 {type.Name} 缺少主键：请用 {nameof(VectorKeyAttribute)} 标注或提供 Id 属性。");
        if (vectors.Count == 0)
        {
            throw new InvalidOperationException(
                $"实体 {type.Name} 没有向量属性：请用 {nameof(VectorFieldAttribute)} 标注至少一个向量字段。");
        }

        Scalars = scalars;
        Vectors = vectors;

        KeyExpression = BuildKeyExpression(invariantToString: false);
        // 写入侧显式用不变文化：数值键的负号等符号在个别文化下 ToString 产生非 ASCII 字符，
        // 而 EF 侧无参 ToString 翻译为 SQL CAST（恒为不变文化），两侧必须对齐。
        KeyGetter = BuildKeyExpression(invariantToString: true).Compile();
    }

    private static bool IsKeyByConvention(PropertyInfo property) =>
        property.Name == "Id" || property.Name == typeof(TEntity).Name + "Id";

    private static readonly HashSet<Type> SupportedKeyTypes =
    [
        typeof(string), typeof(int), typeof(long), typeof(Guid),
    ];

    private static DataType MapKeyProperty(PropertyInfo property)
    {
        Type propertyType = property.PropertyType;
        if (!SupportedKeyTypes.Contains(propertyType))
        {
            string actual = propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)
                    ? $"可空 {Nullable.GetUnderlyingType(propertyType)!.Name}?（主键不应可空）"
                    : propertyType.Name;
            throw new NotSupportedException(
                $"主键 {property.Name} 的类型不受支持（实际 {actual}；支持 string/int/long/Guid）。");
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
            if (attribute.Dimension != 0)
            {
                throw new NotSupportedException($"稀疏向量属性 {property.Name} 不需要 Dimension（保持 0）。");
            }

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

    private Expression<Func<TEntity, string>> BuildKeyExpression(bool invariantToString)
    {
        // string 键直接取属性；数值/Guid 键调用实例 ToString()。
        // EF 翻译路径（invariantToString=false）必须用无参 ToString()——EF Core 仅翻译该重载（SQL CAST 恒为不变文化）。
        // 键类型为封闭集合（int/long/Guid），经 typeof(具体类型) 取 MethodInfo 满足裁剪分析器。
        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression body = Expression.Property(parameter, Key.Property);
        if (Key.Property.PropertyType != typeof(string))
        {
            MethodInfo toString = GetKeyToStringMethod(Key.Property.PropertyType, invariantToString)
                ?? throw new InvalidOperationException($"键类型 {Key.Property.PropertyType.Name} 缺少 ToString()。");
            body = invariantToString
                ? Expression.Call(body, toString, Expression.Constant(CultureInfo.InvariantCulture, typeof(IFormatProvider)))
                : Expression.Call(body, toString);

            if (!invariantToString && Key.Property.PropertyType == typeof(Guid))
            {
                // SQL Server 把 Guid 的 CONVERT 翻译成大写，而写入侧 Guid.ToString() 恒为小写；
                // 查询侧统一 ToLower()（EF 可翻译为 LOWER）保证两侧命中。输入已是十六进制小写，文化无关。
                body = Expression.Call(body, ToLowerMethod);
            }
        }

        return Expression.Lambda<Func<TEntity, string>>(body, parameter);
    }

    private static readonly MethodInfo ToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static MethodInfo? GetKeyToStringMethod(Type keyType, bool invariantToString)
    {
        if (!invariantToString || keyType == typeof(Guid))
        {
            // Guid.ToString() 无文化差异；EF 翻译只认无参重载。
            return keyType == typeof(int) ? typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes)
                : keyType == typeof(long) ? typeof(long).GetMethod(nameof(long.ToString), Type.EmptyTypes)
                : typeof(Guid).GetMethod(nameof(Guid.ToString), Type.EmptyTypes);
        }

        return keyType == typeof(int)
            ? typeof(int).GetMethod(nameof(int.ToString), [typeof(IFormatProvider)])
            : keyType == typeof(long)
                ? typeof(long).GetMethod(nameof(long.ToString), [typeof(IFormatProvider)])
                : typeof(Guid).GetMethod(nameof(Guid.ToString), Type.EmptyTypes);
    }

    internal Doc ToDoc(TEntity entity)
    {
        string id = KeyGetter(entity);
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException(
                $"实体 {typeof(TEntity).Name} 的键属性 {Key.Property.Name} 产生了 null/空主键，无法写入向量集合。", nameof(entity));
        }

        var doc = new Doc(id);
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
                (uint)vector.Dimension, nullable: IsNullable(vector.Property),
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

    internal readonly record struct PropertyMapping(PropertyInfo Property, DataType DataType, int Dimension);
}
