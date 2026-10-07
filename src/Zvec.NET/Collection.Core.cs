using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>
/// 已打开的集合（对齐 Python zvec.Collection）。通过 <see cref="Zvec.CreateAndOpen"/> / <see cref="Zvec.Open"/> 获取。
/// 同步 API 直接映射原生调用；Async 版本为 Task.Run 薄包装。
/// 托管侧对同一实例的并发调用是安全的：每次原生调用持有句柄租约（DangerousAddRef），
/// 并发 <see cref="Close"/> 会推迟到最后一个在途调用结束（SafeHandle 引用计数语义）；
/// 文档迭代（IterateDocs/IterateDocsAsync）在整个枚举期间持有租约。
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

    /// <summary>集合是否已关闭/销毁。</summary>
    public bool IsClosed => _handle.IsClosed;

    /// <summary>
    /// 原生调用期间的集合句柄租约：DangerousAddRef 与 DangerousRelease 配对，
    /// 防止并发 Close/Dispose 在 P/Invoke 执行中途释放原生集合。
    /// SafeHandle 保证引用计数未归零时不会真正执行 close；已关闭时抛 <see cref="ObjectDisposedException"/>。
    /// </summary>
    private readonly struct HandleLease : IDisposable
    {
        private readonly CollectionHandle _handle;

        public IntPtr Ptr { get; }

        private HandleLease(CollectionHandle handle)
        {
            _handle = handle;
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException(nameof(Collection), "集合已关闭。");
            }

            Ptr = handle.DangerousGetHandle();
        }

        internal static HandleLease Acquire(CollectionHandle handle) => new(handle);

        public void Dispose()
        {
            if (_handle is not null)
            {
                _handle.DangerousRelease();
            }
        }
    }

    private HandleLease AcquireLease() => HandleLease.Acquire(_handle);

    /// <summary>
    /// 布尔过滤表达式（如 "age &gt; 30"，由引擎解析求值）。
    /// 非参数化接口——若表达式来源不可信，调用方必须自行转义/校验。
    /// </summary>
    private static string ValidateExpression(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

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
        ArgumentNullException.ThrowIfNull(value, paramName);

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

    /// <summary>插入单个文档。</summary>
    /// <param name="doc">文档。</param>
    public WriteResult Insert(Doc doc) => Insert([doc])[0];

    /// <summary>插入文档（ID 必须唯一）。</summary>
    /// <param name="docs">文档集合。</param>
    public WriteResult[] Insert(IEnumerable<Doc> docs) => WriteDocs(docs, WriteMode.Insert);

    /// <summary>更新单个文档。</summary>
    /// <param name="doc">文档。</param>
    public WriteResult Update(Doc doc) => Update([doc])[0];

    /// <summary>更新文档的指定字段，其余字段保持不变。</summary>
    /// <param name="docs">文档集合。</param>
    public WriteResult[] Update(IEnumerable<Doc> docs) => WriteDocs(docs, WriteMode.Update);

    /// <summary>Upsert 单个文档。</summary>
    /// <param name="doc">文档。</param>
    public WriteResult Upsert(Doc doc) => Upsert([doc])[0];

    /// <summary>插入或更新（按 ID）。</summary>
    /// <param name="docs">文档集合。</param>
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
            using var lease = AcquireLease();
            fixed (IntPtr* docPtrs = nativeDocs)
            {
                int err = mode switch
                {
                    WriteMode.Insert => NativeMethods.zvec_collection_insert_with_results(
                        lease.Ptr, docPtrs, (nuint)docList.Length, out results, out count),
                    WriteMode.Update => NativeMethods.zvec_collection_update_with_results(
                        lease.Ptr, docPtrs, (nuint)docList.Length, out results, out count),
                    _ => NativeMethods.zvec_collection_upsert_with_results(
                        lease.Ptr, docPtrs, (nuint)docList.Length, out results, out count),
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

    /// <summary>删除单个文档。</summary>
    /// <param name="id">文档主键。</param>
    public WriteResult Delete(string id) => Delete([id])[0];

    /// <summary>按 ID 删除文档。</summary>
    /// <param name="ids">主键集合。</param>
    public WriteResult[] Delete(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        string[] idList = [.. ids];
        if (idList.Length == 0)
        {
            return [];
        }

        using var lease = AcquireLease();
        using var arena = new NativeArena();
        byte** pks = arena.AllocUtf8Array(idList, out nuint count);
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_delete_with_results(
            lease.Ptr, pks, count, out IntPtr results, out nuint resultCount));

        return ReadWriteResults(results, resultCount);
    }

    /// <summary>
    /// 按布尔表达式删除匹配文档（如 "age &gt; 30"）。表达式由引擎解析；
    /// 非参数化接口——若表达式来源不可信，调用方必须自行校验。
    /// </summary>
    public void DeleteByFilter(string filter)
    {
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_delete_by_filter(
            lease.Ptr, ValidateExpression(filter, nameof(filter))));
    }
}
