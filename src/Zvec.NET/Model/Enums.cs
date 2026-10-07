using Zvec.NET.Interop;

// CA1720（标识符不宜含类型名）：DataType 枚举成员名对齐 Python SDK 与 C API 的命名
// （String/Int32/Float 等），重命名会破坏跨语言 API 一致性，属有意保留。
#pragma warning disable CA1720

namespace Zvec.NET;

/// <summary>字段数据类型（对齐 Python zvec.DataType）。</summary>
public enum DataType
{
    /// <summary>未定义。</summary>
    Undefined = (int)NativeTypes.DataTypeUndefined,
    /// <summary>二进制（byte[]）。</summary>
    Binary = (int)NativeTypes.DataTypeBinary,
    /// <summary>UTF-8 字符串。</summary>
    String = (int)NativeTypes.DataTypeString,
    /// <summary>布尔。</summary>
    Bool = (int)NativeTypes.DataTypeBool,
    /// <summary>32 位有符号整数。</summary>
    Int32 = (int)NativeTypes.DataTypeInt32,
    /// <summary>64 位有符号整数。</summary>
    Int64 = (int)NativeTypes.DataTypeInt64,
    /// <summary>32 位无符号整数。</summary>
    UInt32 = (int)NativeTypes.DataTypeUInt32,
    /// <summary>64 位无符号整数。</summary>
    UInt64 = (int)NativeTypes.DataTypeUInt64,
    /// <summary>32 位浮点。</summary>
    Float = (int)NativeTypes.DataTypeFloat,
    /// <summary>64 位浮点。</summary>
    Double = (int)NativeTypes.DataTypeDouble,
    /// <summary>32 位二进制向量（v0.7.0 C API 未在托管层开放读写）。</summary>
    VectorBinary32 = (int)NativeTypes.DataTypeVectorBinary32,
    /// <summary>64 位二进制向量（v0.7.0 C API 未在托管层开放读写）。</summary>
    VectorBinary64 = (int)NativeTypes.DataTypeVectorBinary64,
    /// <summary>FP16 稠密向量（Half[] 写入，读回为 float[]）。</summary>
    VectorFp16 = (int)NativeTypes.DataTypeVectorFp16,
    /// <summary>FP32 稠密向量（float[]）。</summary>
    VectorFp32 = (int)NativeTypes.DataTypeVectorFp32,
    /// <summary>FP64 稠密向量（double[]）。</summary>
    VectorFp64 = (int)NativeTypes.DataTypeVectorFp64,
    /// <summary>INT4 量化向量（v0.7.0 C API 未在托管层开放读写）。</summary>
    VectorInt4 = (int)NativeTypes.DataTypeVectorInt4,
    /// <summary>INT8 量化向量（sbyte[] 写入，读回为 float[]）。</summary>
    VectorInt8 = (int)NativeTypes.DataTypeVectorInt8,
    /// <summary>INT16 向量（v0.7.0 C API 未在托管层开放读写）。</summary>
    VectorInt16 = (int)NativeTypes.DataTypeVectorInt16,
    /// <summary>FP16 稀疏向量（SparseVector）。</summary>
    SparseVectorFp16 = (int)NativeTypes.DataTypeSparseVectorFp16,
    /// <summary>FP32 稀疏向量（SparseVector）。</summary>
    SparseVectorFp32 = (int)NativeTypes.DataTypeSparseVectorFp32,
    /// <summary>二进制数组（v0.7.0 托管层未开放）。</summary>
    ArrayBinary = (int)NativeTypes.DataTypeArrayBinary,
    /// <summary>字符串数组（string[]）。</summary>
    ArrayString = (int)NativeTypes.DataTypeArrayString,
    /// <summary>布尔数组（仅支持写入；C API 读回为位打包且丢失长度）。</summary>
    ArrayBool = (int)NativeTypes.DataTypeArrayBool,
    /// <summary>int[] 数组。</summary>
    ArrayInt32 = (int)NativeTypes.DataTypeArrayInt32,
    /// <summary>long[] 数组。</summary>
    ArrayInt64 = (int)NativeTypes.DataTypeArrayInt64,
    /// <summary>uint[] 数组。</summary>
    ArrayUInt32 = (int)NativeTypes.DataTypeArrayUInt32,
    /// <summary>ulong[] 数组。</summary>
    ArrayUInt64 = (int)NativeTypes.DataTypeArrayUInt64,
    /// <summary>float[] 数组。</summary>
    ArrayFloat = (int)NativeTypes.DataTypeArrayFloat,
    /// <summary>double[] 数组。</summary>
    ArrayDouble = (int)NativeTypes.DataTypeArrayDouble,
}

/// <summary>索引类型。</summary>
public enum IndexType
{
    /// <summary>未建索引。</summary>
    Undefined = (int)NativeTypes.IndexTypeUndefined,
    /// <summary>HNSW 图索引。</summary>
    Hnsw = (int)NativeTypes.IndexTypeHnsw,
    /// <summary>IVF 倒排文件索引。</summary>
    Ivf = (int)NativeTypes.IndexTypeIvf,
    /// <summary>Flat 精确检索（暴力）。</summary>
    Flat = (int)NativeTypes.IndexTypeFlat,
    /// <summary>HNSW + RaBitQ 量化。</summary>
    HnswRabitq = (int)NativeTypes.IndexTypeHnswRabitq,
    /// <summary>DiskANN 磁盘索引。</summary>
    DiskAnn = (int)NativeTypes.IndexTypeDiskAnn,
    /// <summary>Vamana 图索引。</summary>
    Vamana = (int)NativeTypes.IndexTypeVamana,
    /// <summary>IVF + RaBitQ 量化。</summary>
    IvfRabitq = (int)NativeTypes.IndexTypeIvfRabitq,
    /// <summary>标量字段倒排索引。</summary>
    Invert = (int)NativeTypes.IndexTypeInvert,
    /// <summary>全文（BM25）索引。</summary>
    Fts = (int)NativeTypes.IndexTypeFts,
}

/// <summary>距离度量类型。</summary>
public enum MetricType
{
    /// <summary>未指定（由引擎按索引默认选择）。</summary>
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
    /// <summary>不量化。</summary>
    Undefined = (int)NativeTypes.QuantizeTypeUndefined,
    /// <summary>FP16 量化。</summary>
    Fp16 = (int)NativeTypes.QuantizeTypeFp16,
    /// <summary>INT8 量化。</summary>
    Int8 = (int)NativeTypes.QuantizeTypeInt8,
    /// <summary>INT4 量化。</summary>
    Int4 = (int)NativeTypes.QuantizeTypeInt4,
    /// <summary>RaBitQ 量化。</summary>
    Rabitq = (int)NativeTypes.QuantizeTypeRabitq,
}

/// <summary>日志级别。</summary>
public enum LogLevel
{
    /// <summary>调试。</summary>
    Debug = 0,
    /// <summary>信息。</summary>
    Info = 1,
    /// <summary>警告（默认）。</summary>
    Warn = 2,
    /// <summary>错误。</summary>
    Error = 3,
    /// <summary>致命。</summary>
    Fatal = 4,
}

/// <summary>日志输出类型。</summary>
public enum LogType
{
    /// <summary>控制台输出（默认）。</summary>
    Console = 0,
    /// <summary>文件输出。</summary>
    File = 1,
}

#pragma warning restore CA1720
