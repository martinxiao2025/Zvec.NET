using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的 Fetch（按主键取回文档）能力。</summary>
public sealed unsafe partial class Collection
{
    /// <summary>按单个主键取回文档。</summary>
    /// <param name="id">主键。</param>
    /// <param name="outputFields">仅取回的标量字段；null = 全部。</param>
    /// <param name="includeVector">是否取回向量。</param>
    /// <returns>主键到文档的映射；未命中不包含该键。</returns>
    public Dictionary<string, Doc> Fetch(string id, IReadOnlyList<string>? outputFields = null, bool includeVector = true) =>
        FetchInternal([id], includeVector, outputFields);

    /// <summary>按主键批量取回文档。</summary>
    /// <param name="ids">主键集合。</param>
    /// <param name="outputFields">仅取回的标量字段；null = 全部。</param>
    /// <param name="includeVector">是否取回向量。</param>
    /// <returns>主键到文档的映射；未命中不包含该键。</returns>
    public Dictionary<string, Doc> Fetch(IEnumerable<string> ids, IReadOnlyList<string>? outputFields = null, bool includeVector = true)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return FetchInternal([.. ids], includeVector, outputFields);
    }

    private Dictionary<string, Doc> FetchInternal(string[] ids, bool includeVector, IReadOnlyList<string>? outputFields)
    {
        foreach (string? id in ids)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("主键不能为 null 或空串。", nameof(ids));
            }
        }

        using var lease = AcquireLease();
        using var arena = new NativeArena();
        byte** keys = arena.AllocUtf8Array(ids, out nuint count);
        nuint fieldCount = 0;
        byte** fields = outputFields is null
            ? null
            : arena.AllocUtf8Array(outputFields.ToArray(), out fieldCount);
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_fetch(
            lease.Ptr, keys, count, fields, fieldCount, includeVector, out IntPtr documents, out nuint foundCount));

        var docPointers = new IntPtr[(int)foundCount];
        Marshal.Copy(documents, docPointers, 0, (int)foundCount);
        Dictionary<string, Doc> result = new((int)foundCount);
        try
        {
            foreach (IntPtr docPtr in docPointers)
            {
                Doc doc = DocCodec.ReadDoc(docPtr, Schema);
                result[doc.Id] = doc;
            }
        }
        finally
        {
            NativeMethods.zvec_docs_free(documents, foundCount);
        }

        return result;
    }
}
