using System.Runtime.InteropServices;
using System.Text;

namespace Zvec.NET.Interop;

/// <summary>Doc 的原生编码/解码。字段值布局遵循 c_api.cc 的 extract_* 约定。</summary>
internal static unsafe class DocCodec
{
    /// <summary>编码缓冲超过此字节数时退到非托管堆，避免大载荷打爆线程栈。</summary>
    private const int StackBufferSize = 1024;

    /// <summary>
    /// 临时编码缓冲：小载荷复用调用方 stackalloc 的 <see cref="StackBufferSize"/> 栈内存，
    /// 大载荷改用 AllocHGlobal。必须以 using/Dispose 释放。持有指向调用方栈帧的指针，
    /// 故声明为 ref struct 禁止逃逸。
    /// </summary>
    private ref struct TempNativeBuffer(int byteCount, byte* stackBuffer)
    {
        private readonly IntPtr _heap = byteCount > StackBufferSize
            ? Marshal.AllocHGlobal(byteCount)
            : IntPtr.Zero;

        public byte* Pointer => _heap == IntPtr.Zero ? stackBuffer : (byte*)_heap;

        public void Dispose()
        {
            if (_heap != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_heap);
            }
        }
    }
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
                AddScalar(doc, name, dataType, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.Int64:
                AddScalar(doc, name, dataType, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.UInt32:
                AddScalar(doc, name, dataType, Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.UInt64:
                AddScalar(doc, name, dataType, Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.Float:
                AddScalar(doc, name, dataType, Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.Double:
                AddScalar(doc, name, dataType, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DataType.String:
            {
                string str = (string)value;
                int byteCount = Encoding.UTF8.GetByteCount(str);
                byte* stackBuffer = stackalloc byte[StackBufferSize];
                using var buffer = new TempNativeBuffer(byteCount, stackBuffer);
                if (byteCount > 0)
                {
                    Encoding.UTF8.GetBytes(str, new Span<byte>(buffer.Pointer, byteCount));
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(doc, name, (uint)dataType, buffer.Pointer, (nuint)byteCount));
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
                // 引擎写入侧启发式（c_api.cc）：value_size 为指针大小倍数时按 zvec_string_t** 指针数组
                // 解释，否则按 NUL 结尾字符串拼接解释。拼接总字节数恰为 8 倍数时会被误判为指针数组
                // 导致野指针解引用，故统一采用指针数组布局（对任意长度/内容稳定）：
                // [zvec_string_t* 数组][zvec_string_t 结构数组][UTF-8 字节区]，前两段均为 8 字节对齐。
                string[] items = value as string[] ?? ((IReadOnlyCollection<object>)value).Cast<string>().ToArray();
                int stringBytes = 0;
                foreach (string item in items)
                {
                    if (item is null)
                    {
                        throw new ArgumentException($"字段 {name} 的字符串数组包含 null 元素。", nameof(value));
                    }

                    stringBytes = checked(stringBytes + Encoding.UTF8.GetByteCount(item));
                }

                int structsOffset = checked(items.Length * sizeof(nuint));
                int bytesOffset = checked(structsOffset + items.Length * sizeof(ZvecString));
                int totalBytes = checked(bytesOffset + stringBytes);
                byte* stackBuffer = stackalloc byte[StackBufferSize];
                using var buffer = new TempNativeBuffer(totalBytes, stackBuffer);
                ZvecString* structs = (ZvecString*)(buffer.Pointer + structsOffset);
                byte* bytes = buffer.Pointer + bytesOffset;
                int offset = 0;
                for (int i = 0; i < items.Length; i++)
                {
                    int written = items[i].Length == 0 ? 0
                        : Encoding.UTF8.GetBytes(items[i], new Span<byte>(bytes + offset, stringBytes - offset));
                    structs[i].Data = (IntPtr)(bytes + offset);
                    structs[i].Length = (nuint)written;
                    structs[i].Capacity = (nuint)written;
                    ((ZvecString**)buffer.Pointer)[i] = structs + i;
                    offset += written;
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                    doc, name, (uint)dataType, buffer.Pointer, (nuint)checked(items.Length * sizeof(nuint))));
                break;
            }
            case DataType.ArrayBool:
            {
                bool[] items = value as bool[] ?? CastToArray<bool>(value);
                fixed (bool* p = items)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)checked(items.Length * sizeof(bool))));
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
            NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                doc, name, (uint)dataType, p, (nuint)checked(values.Length * sizeof(T))));
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
                        doc, name, (uint)dataType, p, (nuint)checked(vector.Length * sizeof(float))));
                }

                break;
            }
            case DataType.VectorFp64:
            {
                double[] vector = value as double[] ?? CastToArray<double>(value);
                fixed (double* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)checked(vector.Length * sizeof(double))));
                }

                break;
            }
            case DataType.VectorFp16:
            {
                Half[] vector = value as Half[] ?? CastToArray<Half>(value);
                fixed (Half* p = vector)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                        doc, name, (uint)dataType, p, (nuint)checked(vector.Length * sizeof(Half))));
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
                int byteCount = checked(4 + nnz * 8);
                byte* stackBuffer = stackalloc byte[StackBufferSize];
                using var buffer = new TempNativeBuffer(byteCount, stackBuffer);
                *(uint*)buffer.Pointer = (uint)nnz;
                uint* indices = (uint*)(buffer.Pointer + 4);
                float* values = (float*)(indices + nnz);
                for (int i = 0; i < nnz; i++)
                {
                    indices[i] = sparse.Indices[i];
                    values[i] = sparse.Values[i];
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                    doc, name, (uint)dataType, buffer.Pointer, (nuint)byteCount));
                break;
            }
            case DataType.SparseVectorFp16:
            {
                SparseVector sparse = ToSparse(value);
                int nnz = sparse.Count;
                int byteCount = checked(4 + nnz * 6);
                byte* stackBuffer = stackalloc byte[StackBufferSize];
                using var buffer = new TempNativeBuffer(byteCount, stackBuffer);
                *(uint*)buffer.Pointer = (uint)nnz;
                uint* indices = (uint*)(buffer.Pointer + 4);
                Half* values = (Half*)(buffer.Pointer + 4 + nnz * 4);
                for (int i = 0; i < nnz; i++)
                {
                    indices[i] = sparse.Indices[i];
                    values[i] = (Half)sparse.Values[i];
                }

                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_add_field_by_value(
                    doc, name, (uint)dataType, buffer.Pointer, (nuint)byteCount));
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
                byte[] bytes = new byte[checked(vector.Length * sizeof(float))];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            case DataType.VectorFp64:
            {
                double[] vector = value as double[] ?? CastToArray<double>(value);
                byte[] bytes = new byte[checked(vector.Length * sizeof(double))];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                return bytes;
            }
            case DataType.VectorFp16:
            {
                Half[] vector = value as Half[] ?? CastToArray<Half>(value);
                byte[] bytes = new byte[checked(vector.Length * sizeof(Half))];
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

    /// <summary>从原生 Doc 读取托管 Doc。schema 提供字段类型。
    /// C API 无法区分"无得分"与"得分 0"：scored=true（查询结果路径）总是填充 Score——L2 完美匹配、
    /// IP 正交等合法 0 分不会丢失；scored=false（Fetch/迭代路径）一律置 null（存储文档本无得分）。</summary>
    internal static Doc ReadDoc(IntPtr nativeDoc, CollectionSchema schema, bool scored = true)
    {
        string id = NativeUtil.PtrToUtf8Required(NativeMethods.zvec_doc_get_pk_pointer(nativeDoc));
        var doc = new Doc(id, scored ? NativeMethods.zvec_doc_get_score(nativeDoc) : null);

        NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_names(nativeDoc, out IntPtr namesPtr, out nuint count));
        try
        {
            // 无字段的文档（如纯向量投影）可能返回 NULL 指针 + count=0：先判空再拷贝。
            if (count > 0 && namesPtr != IntPtr.Zero)
            {
                var names = new IntPtr[checked((int)count)];
                Marshal.Copy(namesPtr, names, 0, (int)count);

                foreach (IntPtr namePtr in names)
                {
                    string fieldName = NativeUtil.PtrToUtf8Required(namePtr);
                    if (schema.Vector(fieldName) is { } vectorSchema)
                    {
                        object? vectorValue = DecodeVector(nativeDoc, fieldName, vectorSchema.DataType);
                        if (vectorValue is not null)
                        {
                            doc.Vectors[fieldName] = vectorValue;
                        }
                    }
                    else if (schema.Field(fieldName) is { } fieldSchema)
                    {
                        doc.Fields[fieldName] = DecodeScalar(nativeDoc, fieldName, fieldSchema.DataType);
                    }
                }
            }
        }
        finally
        {
            NativeMethods.zvec_free_str_array(namesPtr, count);
        }

        return doc;
    }

    /// <summary>读取标量字段并解引用为首个元素（Bool/Int32/Int64/UInt32/UInt64/Float/Double 共用）。</summary>
    private static T ReadScalar<T>(IntPtr doc, string name, DataType dataType) where T : unmanaged
    {
        ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
        return *(T*)ptr;
    }

    private static object? DecodeScalar(IntPtr doc, string name, DataType dataType)
    {
        if (NativeMethods.zvec_doc_is_field_null(doc, name))
        {
            return null;
        }

        switch (dataType)
        {
            case DataType.Bool:
                return ReadScalar<bool>(doc, name, dataType);
            case DataType.Int32:
                return ReadScalar<int>(doc, name, dataType);
            case DataType.Int64:
                return ReadScalar<long>(doc, name, dataType);
            case DataType.UInt32:
                return ReadScalar<uint>(doc, name, dataType);
            case DataType.UInt64:
                return ReadScalar<ulong>(doc, name, dataType);
            case DataType.Float:
                return ReadScalar<float>(doc, name, dataType);
            case DataType.Double:
                return ReadScalar<double>(doc, name, dataType);
            case DataType.String:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out _);
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            case DataType.Binary:
            {
                ReadPointer(doc, name, dataType, out IntPtr ptr, out nuint size);
                byte[] bytes = new byte[checked((int)size)];
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

    private static object? DecodeVector(IntPtr doc, string name, DataType dataType)
    {
        // 标量侧（DecodeScalar）同款 NULL 检查：其他客户端写入的 NULL 向量字段读回为 null，而非异常。
        if (NativeMethods.zvec_doc_is_field_null(doc, name))
        {
            return null;
        }

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
                NativeUtil.ThrowIfError(NativeMethods.zvec_doc_get_field_value_copy(doc, name, (uint)dataType, out IntPtr value, out nuint size));
                try
                {
                    // 读回侧引擎头为 8 字节（低 4B=nnz + 4B 保留）；写入侧为 4 字节头（见 EncodeVector），两侧不对称。
                    // 引擎返回数据不做前置担保，解引用前校验头与载荷长度，坏数据转为异常而非 AV 崩溃进程。
                    if (value == IntPtr.Zero || size < 8)
                    {
                        throw new ZvecException(ZvecErrorCode.InternalError,
                            $"稀疏向量字段 {name} 返回了无效载荷（指针空或 size={size} < 8）。");
                    }

                    byte* buffer = (byte*)value;
                    uint nnz = *(uint*)buffer;
                    bool isFp16 = dataType == DataType.SparseVectorFp16;
                    uint elementSize = isFp16 ? 6u : 8u;
                    if ((nuint)nnz > (size - 8) / elementSize)
                    {
                        throw new ZvecException(ZvecErrorCode.InternalError,
                            $"稀疏向量字段 {name} 的 nnz={nnz} 与载荷大小 {size} 不一致。");
                    }

                    uint* indices = (uint*)(buffer + 8);
                    uint[] indexArray = new uint[nnz];
                    float[] valueArray = new float[nnz];
                    for (int i = 0; i < nnz; i++)
                    {
                        indexArray[i] = indices[i];
                    }

                    if (isFp16)
                    {
                        Half* values = (Half*)(buffer + 8 + nnz * 4);
                        for (int i = 0; i < nnz; i++)
                        {
                            valueArray[i] = (float)values[i];
                        }
                    }
                    else
                    {
                        float* values = (float*)(buffer + 8 + nnz * 4);
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
