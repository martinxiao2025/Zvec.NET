namespace Zvec.NET;

/// <summary>
/// 标量字段定义（对齐 Python FieldSchema）。
/// index_param 支持 InvertIndexParam（倒排索引）或 FtsIndexParam（全文索引，仅 STRING 字段）。
/// </summary>
public sealed class FieldSchema
{
    /// <summary>字段名。</summary>
    public string Name { get; }

    /// <summary>字段数据类型（非向量类型）。</summary>
    public DataType DataType { get; }

    /// <summary>是否允许 NULL。</summary>
    public bool Nullable { get; set; }

    /// <summary>字段索引参数（可选）。</summary>
    public IndexParam? IndexParam { get; set; }

    /// <summary>构造标量字段定义。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="nullable">是否允许 NULL。</param>
    /// <param name="indexParam">索引参数。</param>
    public FieldSchema(string name, DataType dataType, bool nullable = false, IndexParam? indexParam = null)
    {
        if (SchemaUtil.IsVectorDataType(dataType))
        {
            throw new ArgumentException($"FieldSchema 不接受向量类型 {dataType}，请使用 VectorSchema。");
        }

        Name = name;
        DataType = dataType;
        Nullable = nullable;
        IndexParam = indexParam;
    }
}

/// <summary>
/// 向量字段定义（对齐 Python VectorSchema）。
/// dataType 支持 VECTOR_FP16/FP32/FP64/INT8 与 SPARSE_VECTOR_FP16/FP32。
/// </summary>
public sealed class VectorSchema
{
    /// <summary>字段名。</summary>
    public string Name { get; }

    /// <summary>向量数据类型。</summary>
    public DataType DataType { get; }

    /// <summary>是否允许 NULL。</summary>
    public bool Nullable { get; set; }

    /// <summary>稠密向量维度（稀疏向量为 0）。</summary>
    public uint Dimension { get; }

    /// <summary>索引参数（可选）。</summary>
    public IndexParam? IndexParam { get; set; }

    /// <summary>构造向量字段定义。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="dataType">向量类型。</param>
    /// <param name="dimension">稠密维度（稀疏为 0）。</param>
    /// <param name="nullable">是否允许 NULL。</param>
    /// <param name="indexParam">索引参数。</param>
    public VectorSchema(string name, DataType dataType, uint dimension, bool nullable = false, IndexParam? indexParam = null)
    {
        if (!SchemaUtil.IsDenseVectorDataType(dataType) && !SchemaUtil.IsSparseVectorDataType(dataType))
        {
            throw new ArgumentException($"VectorSchema 的 data_type 不支持 {dataType}。");
        }

        if (SchemaUtil.IsSparseVectorDataType(dataType) && dimension != 0)
        {
            throw new ArgumentException("稀疏向量字段不需要 dimension（保持 0）。");
        }

        Name = name;
        DataType = dataType;
        Dimension = dimension;
        Nullable = nullable;
        IndexParam = indexParam;
    }
}

/// <summary>集合 Schema（对齐 Python CollectionSchema）。</summary>
public sealed class CollectionSchema
{
    /// <summary>集合名。</summary>
    public string Name { get; set; }

    /// <summary>标量字段列表。</summary>
    public List<FieldSchema> Fields { get; } = [];

    /// <summary>向量字段列表。</summary>
    public List<VectorSchema> Vectors { get; } = [];

    /// <summary>以集合名构造空 Schema。</summary>
    /// <param name="name">集合名。</param>
    public CollectionSchema(string name)
    {
        Name = name;
    }

    /// <summary>追加标量字段（链式）。</summary>
    /// <param name="field">字段定义。</param>
    public CollectionSchema AddField(FieldSchema field)
    {
        Fields.Add(field);
        return this;
    }

    /// <summary>追加向量字段（链式）。</summary>
    /// <param name="vector">向量字段定义。</param>
    public CollectionSchema AddVector(VectorSchema vector)
    {
        Vectors.Add(vector);
        return this;
    }

    /// <summary>按名查找标量字段。</summary>
    /// <param name="name">字段名。</param>
    public FieldSchema? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>按名查找向量字段。</summary>
    /// <param name="name">字段名。</param>
    public VectorSchema? Vector(string name) => Vectors.FirstOrDefault(v => v.Name == name);
}

internal static class SchemaUtil
{
    public static bool IsDenseVectorDataType(DataType type) => type is DataType.VectorFp16 or DataType.VectorFp32
        or DataType.VectorFp64 or DataType.VectorInt8;

    public static bool IsSparseVectorDataType(DataType type) => type is DataType.SparseVectorFp16 or DataType.SparseVectorFp32;

    public static bool IsVectorDataType(DataType type) => IsDenseVectorDataType(type) || IsSparseVectorDataType(type);
}
