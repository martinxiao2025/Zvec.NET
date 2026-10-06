using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的 Fetch（按主键取回文档）能力。</summary>
public sealed unsafe partial class Collection
{
    public Dictionary<string, Doc> Fetch(string id, IReadOnlyList<string>? outputFields = null, bool includeVector = true) =>
        FetchInternal([id], includeVector, outputFields);

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

        using var arena = new NativeArena();
        byte** keys = arena.AllocUtf8Array(ids, out nuint count);
        nuint fieldCount = 0;
        byte** fields = outputFields is null
            ? null
            : arena.AllocUtf8Array(outputFields.ToArray(), out fieldCount);
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_fetch(
            Handle, keys, count, fields, fieldCount, includeVector, out IntPtr documents, out nuint foundCount));

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
