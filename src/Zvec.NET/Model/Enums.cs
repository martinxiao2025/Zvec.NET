using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>字段数据类型（对齐 Python zvec.DataType）。</summary>
public enum DataType
{
    Undefined = (int)NativeTypes.DataTypeUndefined,
    Binary = (int)NativeTypes.DataTypeBinary,
    String = (int)NativeTypes.DataTypeString,
    Bool = (int)NativeTypes.DataTypeBool,
    Int32 = (int)NativeTypes.DataTypeInt32,
    Int64 = (int)NativeTypes.DataTypeInt64,
    UInt32 = (int)NativeTypes.DataTypeUInt32,
    UInt64 = (int)NativeTypes.DataTypeUInt64,
    Float = (int)NativeTypes.DataTypeFloat,
    Double = (int)NativeTypes.DataTypeDouble,
    VectorBinary32 = (int)NativeTypes.DataTypeVectorBinary32,
    VectorBinary64 = (int)NativeTypes.DataTypeVectorBinary64,
    VectorFp16 = (int)NativeTypes.DataTypeVectorFp16,
    VectorFp32 = (int)NativeTypes.DataTypeVectorFp32,
    VectorFp64 = (int)NativeTypes.DataTypeVectorFp64,
    VectorInt4 = (int)NativeTypes.DataTypeVectorInt4,
    VectorInt8 = (int)NativeTypes.DataTypeVectorInt8,
    VectorInt16 = (int)NativeTypes.DataTypeVectorInt16,
    SparseVectorFp16 = (int)NativeTypes.DataTypeSparseVectorFp16,
    SparseVectorFp32 = (int)NativeTypes.DataTypeSparseVectorFp32,
    ArrayBinary = (int)NativeTypes.DataTypeArrayBinary,
    ArrayString = (int)NativeTypes.DataTypeArrayString,
    ArrayBool = (int)NativeTypes.DataTypeArrayBool,
    ArrayInt32 = (int)NativeTypes.DataTypeArrayInt32,
    ArrayInt64 = (int)NativeTypes.DataTypeArrayInt64,
    ArrayUInt32 = (int)NativeTypes.DataTypeArrayUInt32,
    ArrayUInt64 = (int)NativeTypes.DataTypeArrayUInt64,
    ArrayFloat = (int)NativeTypes.DataTypeArrayFloat,
    ArrayDouble = (int)NativeTypes.DataTypeArrayDouble,
}

/// <summary>索引类型。</summary>
public enum IndexType
{
    Undefined = (int)NativeTypes.IndexTypeUndefined,
    Hnsw = (int)NativeTypes.IndexTypeHnsw,
    Ivf = (int)NativeTypes.IndexTypeIvf,
    Flat = (int)NativeTypes.IndexTypeFlat,
    HnswRabitq = (int)NativeTypes.IndexTypeHnswRabitq,
    DiskAnn = (int)NativeTypes.IndexTypeDiskAnn,
    Vamana = (int)NativeTypes.IndexTypeVamana,
    IvfRabitq = (int)NativeTypes.IndexTypeIvfRabitq,
    Invert = (int)NativeTypes.IndexTypeInvert,
    Fts = (int)NativeTypes.IndexTypeFts,
}

/// <summary>距离度量类型。</summary>
public enum MetricType
{
    Undefined = (int)NativeTypes.MetricTypeUndefined,
    /// <summary>欧氏距离平方。</summary>
    L2 = (int)NativeTypes.MetricTypeL2,
    /// <summary>内积。</summary>
    Ip = (int)NativeTypes.MetricTypeIp,
    /// <summary>余弦相似度。</summary>
    Cosine = (int)NativeTypes.MetricTypeCosine,
    /// <summary>负内积排序的 L2。</summary>
    MipsL2 = (int)NativeTypes.MetricTypeMipsL2,
}

/// <summary>量化类型。</summary>
public enum QuantizeType
{
    Undefined = (int)NativeTypes.QuantizeTypeUndefined,
    Fp16 = (int)NativeTypes.QuantizeTypeFp16,
    Int8 = (int)NativeTypes.QuantizeTypeInt8,
    Int4 = (int)NativeTypes.QuantizeTypeInt4,
    Rabitq = (int)NativeTypes.QuantizeTypeRabitq,
}

/// <summary>日志级别。</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Fatal = 4,
}

/// <summary>日志输出类型。</summary>
public enum LogType
{
    Console = 0,
    File = 1,
}
