using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的全量文档迭代能力。</summary>
public sealed unsafe partial class Collection
{
    /// <summary>全量快照迭代（对齐 Python iter_docs；枚举期间 DDL/destroy 受限）。
    /// 枚举全程持有集合句柄租约：并发 Close/Dispose 推迟到迭代结束后才真正执行原生 close，
    /// 避免引擎因迭代器未关闭而 close 失败（SafeHandle 记为释放失败将永久泄漏原生集合）。</summary>
    /// <param name="outputFields">仅迭代的标量字段；null = 全部（不支持空列表）。</param>
    /// <param name="includeVector">是否迭代向量。</param>
    public IEnumerable<Doc> IterateDocs(IReadOnlyList<string>? outputFields = null, bool includeVector = true)
    {
        ValidateOutputFields(outputFields);
        using var collectionLease = AcquireLease();
        IntPtr iterator = CreateDocIterator(outputFields, includeVector);
        var handle = new DocIteratorHandle(iterator);
        try
        {
            while (true)
            {
                int err = NativeMethods.zvec_doc_iterator_next(iterator, out IntPtr docPtr);
                NativeUtil.ThrowIfError(err);
                if (docPtr == IntPtr.Zero)
                {
                    break;
                }

                var docHandle = new DocHandle(docPtr);
                try
                {
                    yield return DocCodec.ReadDoc(docPtr, Schema, scored: false);
                }
                finally
                {
                    docHandle.Dispose();
                }
            }
        }
        finally
        {
            handle.Dispose();
        }
    }

    internal IntPtr CreateDocIterator(IReadOnlyList<string>? outputFields, bool includeVector)
    {
        IntPtr options = NativeMethods.zvec_iterator_options_create();
        NativeUtil.ThrowIfNull(options, "iterator options");
        try
        {
            if (outputFields is not null)
            {
                using var arena = new NativeArena();
                byte** fields = arena.AllocUtf8Array(outputFields, out nuint count);
                NativeUtil.ThrowIfError(NativeMethods.zvec_iterator_options_set_output_fields(options, fields, count));
            }

            using var lease = AcquireLease();
            NativeUtil.ThrowIfError(NativeMethods.zvec_iterator_options_set_include_vector(options, includeVector));
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_create_iterator(lease.Ptr, options, out IntPtr iterator));
            return iterator;
        }
        finally
        {
            NativeMethods.zvec_iterator_options_destroy(options);
        }
    }
}
