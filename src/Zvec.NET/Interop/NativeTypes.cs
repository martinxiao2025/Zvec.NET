using System.Runtime.InteropServices;

namespace Zvec.NET.Interop;

/// <summary>与 c_api.h 对应的常量与结构体定义（数据类型/索引类型/距离度量/量化类型的数值码）。</summary>
internal static class NativeTypes
{
    // zvec_data_type_t
    public const uint DataTypeUndefined = 0;
    public const uint DataTypeBinary = 1;
    public const uint DataTypeString = 2;
    public const uint DataTypeBool = 3;
    public const uint DataTypeInt32 = 4;
    public const uint DataTypeInt64 = 5;
    public const uint DataTypeUInt32 = 6;
    public const uint DataTypeUInt64 = 7;
    public const uint DataTypeFloat = 8;
    public const uint DataTypeDouble = 9;
    public const uint DataTypeVectorBinary32 = 20;
    public const uint DataTypeVectorBinary64 = 21;
    public const uint DataTypeVectorFp16 = 22;
    public const uint DataTypeVectorFp32 = 23;
    public const uint DataTypeVectorFp64 = 24;
    public const uint DataTypeVectorInt4 = 25;
    public const uint DataTypeVectorInt8 = 26;
    public const uint DataTypeVectorInt16 = 27;
    public const uint DataTypeSparseVectorFp16 = 30;
    public const uint DataTypeSparseVectorFp32 = 31;
    public const uint DataTypeArrayBinary = 40;
    public const uint DataTypeArrayString = 41;
    public const uint DataTypeArrayBool = 42;
    public const uint DataTypeArrayInt32 = 43;
    public const uint DataTypeArrayInt64 = 44;
    public const uint DataTypeArrayUInt32 = 45;
    public const uint DataTypeArrayUInt64 = 46;
    public const uint DataTypeArrayFloat = 47;
    public const uint DataTypeArrayDouble = 48;

    // zvec_index_type_t
    public const uint IndexTypeUndefined = 0;
    public const uint IndexTypeHnsw = 1;
    public const uint IndexTypeIvf = 2;
    public const uint IndexTypeFlat = 3;
    public const uint IndexTypeHnswRabitq = 4;
    public const uint IndexTypeDiskAnn = 5;
    public const uint IndexTypeVamana = 6;
    public const uint IndexTypeIvfRabitq = 7;
    public const uint IndexTypeInvert = 10;
    public const uint IndexTypeFts = 11;

    // zvec_metric_type_t
    public const uint MetricTypeUndefined = 0;
    public const uint MetricTypeL2 = 1;
    public const uint MetricTypeIp = 2;
    public const uint MetricTypeCosine = 3;
    public const uint MetricTypeMipsL2 = 4;

    // zvec_quantize_type_t
    public const uint QuantizeTypeUndefined = 0;
    public const uint QuantizeTypeFp16 = 1;
    public const uint QuantizeTypeInt8 = 2;
    public const uint QuantizeTypeInt4 = 3;
    public const uint QuantizeTypeRabitq = 4;
}

/// <summary>zvec_error_details_t 结构体映射。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ZvecErrorDetails
{
    public int Code;          // zvec_error_code_t
    public IntPtr Message;    // const char*
    public IntPtr File;       // const char*
    public int Line;
    public IntPtr Function;   // const char*
}

/// <summary>zvec_string_array_t 结构体映射（指向 zvec_string_t 数组）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ZvecStringArray
{
    public IntPtr Strings;  // zvec_string_t*
    public nuint Count;
}

/// <summary>zvec_string_t 结构体映射。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ZvecString
{
    public IntPtr Data;   // char*
    public nuint Length;
    public nuint Capacity;
}

/// <summary>zvec_write_result_t 结构体映射（逐文档写结果）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ZvecWriteResult
{
    public int Code;         // zvec_error_code_t
    public IntPtr Message;   // const char*（由 API 分配，随 write_results_free 释放）
}
