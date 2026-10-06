using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>
/// 已打开的集合（对齐 Python zvec.Collection）。通过 <see cref="Zvec.CreateAndOpen"/> / <see cref="Zvec.Open"/> 获取。
/// 同步 API 直接映射原生调用；Async 版本为 Task.Run 薄包装。
/// </summary>
public sealed unsafe partial class Collection : IDisposable
{
    private CollectionHandle _handle;
    private readonly string _path;

    private Collection(CollectionHandle handle, string path, CollectionSchema schema)
    {
        _handle = handle;
        _path = path;
        Schema = schema;
    }

    internal static Collection FromCreate(IntPtr handle, string path, CollectionSchema schema)
    {
        NativeUtil.ThrowIfNull(handle, "collection");
        return new Collection(new CollectionHandle(handle), path, schema);
    }

    internal static Collection FromOpen(IntPtr handle, string path)
    {
        NativeUtil.ThrowIfNull(handle, "collection");
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_get_schema(handle, out IntPtr nativeSchema));
        var schemaHandle = new DelegateHandle(nativeSchema, NativeMethods.zvec_collection_schema_destroy);
        try
        {
            CollectionSchema schema = ParamBuilder.ReadCollectionSchema(nativeSchema);
            return new Collection(new CollectionHandle(handle), path, schema);
        }
        finally
        {
            schemaHandle.Dispose();
        }
    }

    /// <summary>集合路径（打开时传入）。经 <see cref="Zvec.Open"/> 打开时为空串。</summary>
    public string Path => _path;

    /// <summary>集合 Schema 托管镜像。DDL 操作后自动刷新。</summary>
    public CollectionSchema Schema { get; private set; }

    public bool IsClosed => _handle.IsClosed;

    private IntPtr Handle => _handle.IsClosed
        ? throw new ObjectDisposedException(nameof(Collection), "集合已关闭。")
        : _handle.DangerousGetHandle();

    private IntPtr HandleNoAddRef => _handle.DangerousGetHandle();

    /// <summary>
    /// 布尔过滤表达式（如 "age &gt; 30"，由引擎解析求值）。
    /// 非参数化接口——若表达式来源不可信，调用方必须自行转义/校验。
    /// </summary>
    private static string ValidateExpression(string value, string paramName)
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("表达式不能为空。", paramName);
        }

        if (value.Contains('\0'))
        {
            throw new ArgumentException("表达式包含非法字符（NUL）。", paramName);
        }

        return value;
    }

    private static string ValidateIdentifier(string value, string paramName)
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("名称不能为空。", paramName);
        }

        if (value.Contains('\0'))
        {
            throw new ArgumentException("名称包含非法字符（NUL）。", paramName);
        }

        return value;
    }

    /// <summary>查询过滤表达式校验：null 放行（不过滤），空串/含 NUL 拒绝。</summary>
    private static string? ValidateFilter(string? filter)
    {
        if (filter is null)
        {
            return null;
        }

        return ValidateExpression(filter, nameof(filter));
    }

    // =========================================================================
    // DML：写入
    // =========================================================================

    public WriteResult Insert(Doc doc) => Insert([doc])[0];

    /// <summary>插入文档（ID 必须唯一）。</summary>
    public WriteResult[] Insert(IEnumerable<Doc> docs) => WriteDocs(docs, WriteMode.Insert);

    public WriteResult Update(Doc doc) => Update([doc])[0];

    /// <summary>更新文档的指定字段，其余字段保持不变。</summary>
    public WriteResult[] Update(IEnumerable<Doc> docs) => WriteDocs(docs, WriteMode.Update);

    public WriteResult Upsert(Doc doc) => Upsert([doc])[0];

    /// <summary>插入或更新（按 ID）。</summary>
    public WriteResult[] Upsert(IEnumerable<Doc> docs) => WriteDocs(docs, WriteMode.Upsert);

    private WriteResult[] WriteDocs(IEnumerable<Doc> docs, WriteMode mode)
    {
        ArgumentNullException.ThrowIfNull(docs);

        Doc[] docList = [.. docs];
        if (docList.Length == 0)
        {
            return [];
        }

        IntPtr[] nativeDocs = new IntPtr[docList.Length];
        try
        {
            for (int i = 0; i < docList.Length; i++)
            {
                nativeDocs[i] = DocCodec.BuildDoc(docList[i], Schema);
            }

            IntPtr results;
            nuint count;
            fixed (IntPtr* docPtrs = nativeDocs)
            {
                int err = mode switch
                {
                    WriteMode.Insert => NativeMethods.zvec_collection_insert_with_results(
                        Handle, docPtrs, (nuint)docList.Length, out results, out count),
                    WriteMode.Update => NativeMethods.zvec_collection_update_with_results(
                        Handle, docPtrs, (nuint)docList.Length, out results, out count),
                    _ => NativeMethods.zvec_collection_upsert_with_results(
                        Handle, docPtrs, (nuint)docList.Length, out results, out count),
                };
                NativeUtil.ThrowIfError(err);
            }

            return ReadWriteResults(results, count);
        }
        finally
        {
            foreach (IntPtr nativeDoc in nativeDocs)
            {
                if (nativeDoc != IntPtr.Zero)
                {
                    NativeMethods.zvec_doc_destroy(nativeDoc);
                }
            }
        }
    }

    private static WriteResult[] ReadWriteResults(IntPtr results, nuint count)
    {
        var raw = new Span<ZvecWriteResult>((void*)results, (int)count);
        WriteResult[] output = new WriteResult[(int)count];
        try
        {
            for (int i = 0; i < raw.Length; i++)
            {
                int code = raw[i].Code;
                string message = NativeUtil.PtrToUtf8(raw[i].Message) ?? string.Empty;
                output[i] = new WriteResult(code == 0, (ZvecErrorCode)code, message);
            }
        }
        finally
        {
            NativeMethods.zvec_write_results_free(results, count);
        }

        return output;
    }

    private enum WriteMode
    {
        Insert,
        Update,
        Upsert,
    }

    // =========================================================================
    // DML：删除
    // =========================================================================

    public WriteResult Delete(string id) => Delete([id])[0];

    /// <summary>按 ID 删除文档。</summary>
    public WriteResult[] Delete(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        string[] idList = [.. ids];
        if (idList.Length == 0)
        {
            return [];
        }

        using var arena = new NativeArena();
        byte** pks = arena.AllocUtf8Array(idList, out nuint count);
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_delete_with_results(
            Handle, pks, count, out IntPtr results, out nuint resultCount));

        return ReadWriteResults(results, resultCount);
    }

    /// <summary>
    /// 按布尔表达式删除匹配文档（如 "age &gt; 30"）。表达式由引擎解析；
    /// 非参数化接口——若表达式来源不可信，调用方必须自行校验。
    /// </summary>
    public void DeleteByFilter(string filter) =>
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_delete_by_filter(
            Handle, ValidateExpression(filter, nameof(filter))));
}
