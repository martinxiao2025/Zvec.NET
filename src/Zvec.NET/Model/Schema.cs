namespace Zvec.NET;

/// <summary>
/// 标量字段定义（对齐 Python FieldSchema）。
/// index_param 支持 InvertIndexParam（倒排索引）或 FtsIndexParam（全文索引，仅 STRING 字段）。
/// </summary>
public sealed class FieldSchema
{
    public string Name { get; }

    public DataType DataType { get; }

    public bool Nullable { get; set; }

    public IndexParam? IndexParam { get; set; }

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
    public string Name { get; }

    public DataType DataType { get; }

    public bool Nullable { get; set; }

    public uint Dimension { get; }

    public IndexParam? IndexParam { get; set; }

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
    public string Name { get; set; }

    public List<FieldSchema> Fields { get; } = [];

    public List<VectorSchema> Vectors { get; } = [];

    public CollectionSchema(string name)
    {
        Name = name;
    }

    public CollectionSchema AddField(FieldSchema field)
    {
        Fields.Add(field);
        return this;
    }

    public CollectionSchema AddVector(VectorSchema vector)
    {
        Vectors.Add(vector);
        return this;
    }

    public FieldSchema? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    public VectorSchema? Vector(string name) => Vectors.FirstOrDefault(v => v.Name == name);
}

internal static class SchemaUtil
{
    public static bool IsDenseVectorDataType(DataType type) => type is DataType.VectorFp16 or DataType.VectorFp32
        or DataType.VectorFp64 or DataType.VectorInt8;

    public static bool IsSparseVectorDataType(DataType type) => type is DataType.SparseVectorFp16 or DataType.SparseVectorFp32;

    public static bool IsVectorDataType(DataType type) => IsDenseVectorDataType(type) || IsSparseVectorDataType(type);
}
