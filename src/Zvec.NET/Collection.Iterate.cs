using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的全量文档迭代能力。</summary>
public sealed unsafe partial class Collection
{
    /// <summary>全量快照迭代（对齐 Python iter_docs；枚举期间 DDL/destroy 受限）。</summary>
    public IEnumerable<Doc> IterateDocs(IReadOnlyList<string>? outputFields = null, bool includeVector = true)
    {
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
                    yield return DocCodec.ReadDoc(docPtr, Schema);
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
                byte** fields = arena.AllocUtf8Array(outputFields.ToArray(), out nuint count);
                NativeUtil.ThrowIfError(NativeMethods.zvec_iterator_options_set_output_fields(options, fields, count));
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_iterator_options_set_include_vector(options, includeVector));
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_create_iterator(Handle, options, out IntPtr iterator));
            return iterator;
        }
        finally
        {
            NativeMethods.zvec_iterator_options_destroy(options);
        }
    }
}
