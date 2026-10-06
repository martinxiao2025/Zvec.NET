using System.Runtime.InteropServices;
using System.Text;

namespace Zvec.NET.Interop;

/// <summary>Doc 的原生编码/解码。字段值布局遵循 c_api.cc 的 extract_* 约定。</summary>
internal static unsafe class DocCodec
{
    /// <summary>构建原生 Doc（调用方拥有，用 zvec_doc_destroy 释放）。schema 决定每个字段的编码类型。</summary>
    internal static IntPtr BuildDoc(Doc doc, CollectionSchema schema)
    {
        if (string.IsNullOrEmpty(doc.Id))
        {
            throw new ArgumentException("Doc.Id（主键）不能为 null 或空串。", nameof(doc));
        }

        IntPtr native = NativeMethods.zvec_doc_create();
        NativeUtil.ThrowIfNull(native, "doc");
        try
        {
            NativeMethods.zvec_doc_set_pk(native, doc.Id);

            foreach (KeyValuePair<string, object?> pair in doc.Fields)
            {
                FieldSchema? fieldSchema = schema.Field(pair.Key)
                    ?? throw new ArgumentException($"字段 {pair.Key} 不在集合 schema 中。", nameof(doc));

                if (pair.Value is null)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_set_field_null(native, pair.Key));
                    continue;
                }

                EncodeScalar(native, pair.Key, fieldSchema.DataType, pair.Value);
            }

            foreach (KeyValuePair<string, object> pair in doc.Vectors)
            {
                VectorSchema? vectorSchema = schema.Vector(pair.Key)
                    ?? throw new ArgumentException($"向量字段 {pair.Key} 不在集合 schema 中。", nameof(doc));

                EncodeVector(native, pair.Key, vectorSchema.DataType, pair.Value);
            }

            IntPtr result = native;
            native = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (native != IntPtr.Zero)
            {
                NativeMethods.zvec_doc_destroy(native);
            }
        }
    }

    private static void EncodeScalar(IntPtr doc, string name, DataType dataType, object value)
    {
        switch (dataType)
        {
            case DataType.Bool:
                AddScalar(doc, name, dataType, (bool)ConvertTo(value, typeof(bool)));
                break;
            case DataType.Int32:
                AddScalar(doc, name, dataType, Convert.ToInt32(value));
                break;
            case DataType.Int64:
                AddScalar(doc, name, dataType, Convert.ToInt64(value));
                break;
            case DataType.UInt32:
                AddScalar(doc, name, dataType, Convert.ToUInt32(value));
                break;
            case DataType.UInt64:
                AddScalar(doc, name, dataType, Convert.ToUInt64(value));
                break;
            case DataType.Float:
                AddScalar(doc, name, dataType, Convert.ToSingle(value));
                break;
            case DataType.Double:
                AddScalar(doc, name, dataType, Convert.ToDouble(value));
                break;
            case DataType.String:
            {
                string str = (string)value;
                int byteCount = Encoding.UTF8.GetByteCount(str);
                byte* buffer = stackalloc byte[byteCount == 0 ? 1 : byteCount];
                if (byteCount > 0)
                {
                    Encoding.UTF8.GetBytes(str, new Span<byte>(buffer, byteCount));
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, buffer, (nuint)byteCount));
                break;
            }
            case DataType.Binary:
            {
                byte[] bytes = (byte[])value;
                fixed (byte* p = bytes)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, p, (nuint)bytes.Length));
                }

                break;
            }
            case DataType.ArrayString:
            {
                string[] items = value as string[] ?? ((IReadOnlyCollection<object>)value).Cast<string>().ToArray();
                int totalBytes = 1;
                foreach (string item in items)
                {
                    totalBytes += Encoding.UTF8.GetByteCount(item) + 1;
                }

                byte* buffer = stackalloc byte[totalBytes];
                int offset = 0;
                Span<byte> span = new(buffer, totalBytes);
                foreach (string item in items)
                {
                    offset += Encoding.UTF8.GetBytes(item, span[offset..]);
                    span[offset] = 0;
                    offset++;
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, buffer, (nuint)offset));
                break;
            }
            case DataType.ArrayBool:
            {
                bool[] items = value as bool[] ?? CastToArray<bool>(value);
                fixed (bool* p = items)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)(items.Length * sizeof(bool))));
                }

                break;
            }
            case DataType.ArrayInt32:
                AddArray(doc, name, dataType, value as int[] ?? CastToArray<int>(value));
                break;
            case DataType.ArrayInt64:
                AddArray(doc, name, dataType, value as long[] ?? CastToArray<long>(value));
                break;
            case DataType.ArrayUInt32:
                AddArray(doc, name, dataType, value as uint[] ?? CastToArray<uint>(value));
                break;
            case DataType.ArrayUInt64:
                AddArray(doc, name, dataType, value as ulong[] ?? CastToArray<ulong>(value));
                break;
            case DataType.ArrayFloat:
                AddArray(doc, name, dataType, value as float[] ?? CastToArray<float>(value));
                break;
            case DataType.ArrayDouble:
                AddArray(doc, name, dataType, value as double[] ?? CastToArray<double>(value));
                break;
            default:
                throw new NotSupportedException($"暂不支持写入字段类型 {dataType}（字段 {name}）。");
        }
    }

    private static object ConvertTo(object value, Type type) => Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);

    private static T[] CastToArray<T>(object value) => value switch
    {
        IReadOnlyCollection<object> list => list.Cast<T>().ToArray(),
        IEnumerable<T> typed => typed.ToArray(),
        _ => throw new ArgumentException($"无法将 {value.GetType().Name} 转换为 {typeof(T).Name}[]"),
    };

    private static void AddScalar<T>(IntPtr doc, string name, DataType dataType, T value) where T : unmanaged
    {
        NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, &value, (nuint)sizeof(T)));
    }

    private static void AddArray<T>(IntPtr doc, string name, DataType dataType, T[] values) where T : unmanaged
    {
        fixed (T* p = values)
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, p, (nuint)(values.Length * sizeof(T))));
        }
    }

    private static void EncodeVector(IntPtr doc, string name, DataType dataType, object value)
    {
        switch (dataType)
        {
            case DataType.VectorFp32:
            {
                float[] vector = value as float[] ?? CastToArray<float>(value);
                fixed (float* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)(vector.Length * sizeof(float))));
                }

                break;
            }
            case DataType.VectorFp64:
            {
                double[] vector = value as double[] ?? CastToArray<double>(value);
                fixed (double* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)(vector.Length * sizeof(double))));
                }

                break;
            }
            case DataType.VectorFp16:
            {
                Half[] vector = value as Half[] ?? CastToArray<Half>(value);
                fixed (Half* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)(vector.Length * sizeof(Half))));
                }

                break;
            }
            case DataType.VectorInt8:
            {
                sbyte[] vector = value as sbyte[] ?? CastToArray<sbyte>(value);
                fixed (sbyte* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)vector.Length));
                }

                break;
            }
            case DataType.SparseVectorFp32:
            {
                SparseVector sparse = ToSparse(value);
                int nnz = sparse.Count;
                byte* buffer = stackalloc byte[4 + nnz * 8];
                *(uint*)buffer = (uint)nnz;
                uint* indices = (uint*)(buffer + 4);
                float* values = (float*)(indices + nnz);
                for (int i = 0; i < nnz; i++)
                {
                    indices[i] = sparse.Indices[i];
                    values[i] = sparse.Values[i];
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                    doc, name, (uint)dataType, buffer, (nuint)(4 + nnz * 8)));
                break;
            }
            case DataType.SparseVectorFp16:
            {
                SparseVector sparse = ToSparse(value);
                int nnz = sparse.Count;
                byte* buffer = stackalloc byte[4 + nnz * 6];
                *(uint*)buffer = (uint)nnz;
                uint* indices = (uint*)(buffer + 4);
                Half* values = (Half*)(buffer + 4 + nnz * 4);
                for (int i = 0; i < nnz; i++)
                {
                    indices[i] = sparse.Indices[i];
                    values[i] = (Half)sparse.Values[i];
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                    doc, name, (uint)dataType, buffer, (nuint)(4 + nnz * 6)));
                break;
            }
            default:
                throw new NotSupportedException($"暂不支持写入向量类型 {dataType}（字段 {name}）。");
        }
    }

    private static SparseVector ToSparse(object value) => value switch
    {
        SparseVector sparse => sparse,
        IReadOnlyDictionary<long, float> dict => SparseVector.FromDictionary(dict),
        IReadOnlyDictionary<int, float> dict => SparseVector.FromDictionary(
            dict.ToDictionary(kv => (long)kv.Key, kv => kv.Value)),
        _ => throw new ArgumentException($"稀疏向量值需要 SparseVector 或字典，实际为 {value.GetType().Name}。"),
    };

    /// <summary>把稠密向量按字段类型编码为查询字节载荷（FP32/FP64/FP16/INT8 原始元素布局）。</summary>
    internal static byte[] EncodeDenseVectorBytes(object value, DataType dataType)
    {
        switch (dataType)
        {
            case DataType.VectorFp32:
            {
                float[] vector = value as float[] ?? CastToArray<float>(value);
                byte[] bytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            case DataType.VectorFp64:
            {
                double[] vector = value as double[] ?? CastToArray<double>(value);
                byte[] bytes = new byte[vector.Length * sizeof(double)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            case DataType.VectorFp16:
            {
                Half[] vector = value as Half[] ?? CastToArray<Half>(value);
                byte[] bytes = new byte[vector.Length * sizeof(Half)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            case DataType.VectorInt8:
            {
                sbyte[] vector = value as sbyte[] ?? CastToArray<sbyte>(value);
                byte[] bytes = new byte[vector.Length];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            default:
                throw new NotSupportedException($"查询向量仅支持稠密类型（FP32/FP64/FP16/INT8），字段类型为 {dataType}。");
        }
    }

    // =========================================================================
    // 解码
    // =========================================================================

    /// <summary>从原生 Doc 读取托管 Doc。schema 提供字段类型。</summary>
    internal static Doc ReadDoc(IntPtr nativeDoc, CollectionSchema schema)
    {
        string id = NativeUtil.PtrToUtf8Required(NativeMethods.zvec_doc_get_pk_pointer(nativeDoc));
        float score = NativeMethods.zvec_doc_get_score(nativeDoc);
        var doc = new Doc(id, score == 0f && !HasScoreField(nativeDoc) ? null : score);

        NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_names(nativeDoc, out IntPtr namesPtr, out nuint count));
        try
        {
            var names = new IntPtr[(int)count];
            Marshal.Copy(namesPtr, names, 0, (int)count);

            foreach (IntPtr namePtr in names)
            {
                string fieldName = NativeUtil.PtrToUtf8Required(namePtr);
                if (schema.Vector(fieldName) is { } vectorSchema)
                {
                    object vectorValue = DecodeVector(nativeDoc, fieldName, vectorSchema.DataType);
                    doc.Vectors[fieldName] = vectorValue;
                }
                else if (schema.Field(fieldName) is { } fieldSchema)
                {
                    doc.Fields[fieldName] = DecodeScalar(nativeDoc, fieldName, fieldSchema.DataType);
                }
            }
        }
        finally
        {
            NativeMethods.zvec_free_str_array(namesPtr, count);
        }

        return doc;
    }

    private static bool HasScoreField(IntPtr nativeDoc) => NativeMethods.zvec_doc_get_score(nativeDoc) != 0f;

    private static object? DecodeScalar(IntPtr doc, string name, DataType dataType)
    {
        if (NativeMethods.zvec_doc_is_field_null(doc, name))
        {
            return null;
        }

        switch (dataType)
        {
            case DataType.Bool:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(bool*)ptr;
            }
            case DataType.Int32:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(int*)ptr;
            }
            case DataType.Int64:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(long*)ptr;
            }
            case DataType.UInt32:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(uint*)ptr;
            }
            case DataType.UInt64:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(ulong*)ptr;
            }
            case DataType.Float:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(float*)ptr;
            }
            case DataType.Double:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return *(double*)ptr;
            }
            case DataType.String:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            case DataType.Binary:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                byte[] bytes = new byte[(int)size];
                if (size > 0)
                {
                    Marshal.Copy(ptr, bytes, 0, (int)size);
                }

                return bytes;
            }
            case DataType.ArrayString:
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_value_copy(doc, name, (uint)dataType, out IntPtr value, out nuint size));
                try
                {
                    return ParseJoinedStrings((byte*)value, size);
                }
                finally
                {
                    NativeMethods.zvec_free(value);
                }
            }
            case DataType.ArrayBool:
                // C API 读回侧将 bool 数组位打包（每 bit 一个），丢失了元素个数，无法还原。
                throw new NotSupportedException(
                    $"字段 {name}：ARRAY_BOOL 支持写入但暂不支持读回（C API 位打包格式丢失长度信息）。");
            case DataType.ArrayInt32:
                return DecodeArray<int>(doc, name, dataType);
            case DataType.ArrayInt64:
                return DecodeArray<long>(doc, name, dataType);
            case DataType.ArrayUInt32:
                return DecodeArray<uint>(doc, name, dataType);
            case DataType.ArrayUInt64:
                return DecodeArray<ulong>(doc, name, dataType);
            case DataType.ArrayFloat:
                return DecodeArray<float>(doc, name, dataType);
            case DataType.ArrayDouble:
                return DecodeArray<double>(doc, name, dataType);
            default:
                throw new NotSupportedException($"暂支持读取字段类型 {dataType}（字段 {name}）。");
        }
    }

    private static T[] DecodeArray<T>(IntPtr doc, string name, DataType dataType) where T : unmanaged
    {
        ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
        int count = checked((int)(size / (nuint)sizeof(T)));
        T[] items = new T[count];
        if (count > 0)
        {
            new Span<T>((void*)ptr, count).CopyTo(items);
        }

        return items;
    }

    private static object DecodeVector(IntPtr doc, string name, DataType dataType)
    {
        switch (dataType)
        {
            case DataType.VectorFp32:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                return ToArray<float>(ptr, size);
            }
            case DataType.VectorFp64:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                return ToArray<double>(ptr, size);
            }
            case DataType.VectorFp16:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                // FP16 统一转 float 返回，方便调用方计算。
                Half* halfPtr = (Half*)ptr;
                int count = checked((int)(size / (nuint)sizeof(Half)));
                float[] result = new float[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = (float)halfPtr[i];
                }

                return result;
            }
            case DataType.VectorInt8:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                sbyte* sbytePtr = (sbyte*)ptr;
                int count = checked((int)size);
                float[] result = new float[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = sbytePtr[i];
                }

                return result;
            }
            case DataType.SparseVectorFp32:
            case DataType.SparseVectorFp16:
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_value_copy(doc, name, (uint)dataType, out IntPtr value, out _));
                try
                {
                    byte* buffer = (byte*)value;
                    uint nnz = *(uint*)buffer;
                    bool isFp16 = dataType == DataType.SparseVectorFp16;
                    uint* indices = (uint*)(buffer + 4);
                    uint[] indexArray = new uint[nnz];
                    float[] valueArray = new float[nnz];
                    for (int i = 0; i < nnz; i++)
                    {
                        indexArray[i] = indices[i];
                    }

                    if (isFp16)
                    {
                        Half* values = (Half*)(buffer + 4 + nnz * 4);
                        for (int i = 0; i < nnz; i++)
                        {
                            valueArray[i] = (float)values[i];
                        }
                    }
                    else
                    {
                        float* values = (float*)(buffer + 4 + nnz * 4);
                        for (int i = 0; i < nnz; i++)
                        {
                            valueArray[i] = values[i];
                        }
                    }

                    return new SparseVector(indexArray, valueArray);
                }
                finally
                {
                    NativeMethods.zvec_free(value);
                }
            }
            default:
                throw new NotSupportedException($"暂支持读取向量类型 {dataType}（字段 {name}）。");
        }
    }

    private static T[] ToArray<T>(IntPtr ptr, nuint size) where T : unmanaged
    {
        int count = checked((int)(size / (nuint)sizeof(T)));
        T[] result = new T[count];
        if (count > 0)
        {
            new Span<T>((void*)ptr, count).CopyTo(result);
        }

        return result;
    }

    private static void ReadPointer(IntPtr doc, string name, DataType dataType, out IntPtr ptr, out nuint size)
    {
        NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_value_pointer(doc, name, (uint)dataType, out ptr, out size));
        if (ptr == IntPtr.Zero)
        {
            throw new ZvecException(ZvecErrorCode.InternalError, $"字段 {name} 返回了空指针。");
        }
    }

    /// <summary>解析引擎序列化的字符串数组：每项以 NUL 结尾连续拼接，总字节数为 size。</summary>
    private static string[] ParseJoinedStrings(byte* buffer, nuint size)
    {
        if (buffer == null || size == 0)
        {
            return [];
        }

        List<string> items = [];
        nuint pos = 0;
        while (pos < size)
        {
            nuint length = 0;
            while (pos + length < size && buffer[pos + length] != 0)
            {
                length++;
            }

            items.Add(Encoding.UTF8.GetString(buffer + pos, checked((int)length)));
            pos += length + 1;
        }

        return [.. items];
    }
}
