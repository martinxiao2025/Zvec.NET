using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的 DDL、统计与生命周期管理。</summary>
public sealed partial class Collection
{    // =========================================================================
    // 索引 DDL
    // =========================================================================

    /// <summary>为字段创建索引（向量字段用 HnswIndexParam 等，标量字段用 InvertIndexParam/FtsIndexParam）。</summary>
    public void CreateIndex(string fieldName, IndexParam indexParam)
    {
        ArgumentNullException.ThrowIfNull(indexParam);
        ValidateIdentifier(fieldName, nameof(fieldName));

        IntPtr nativeParams = ParamBuilder.BuildIndexParam(indexParam);
        try
        {
            using var lease = AcquireLease();
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_create_index(lease.Ptr, fieldName, nativeParams));
        }
        finally
        {
            NativeMethods.zvec_index_params_destroy(nativeParams);
        }

        RefreshSchema();
    }

    /// <summary>删除字段的索引。</summary>
    /// <param name="fieldName">字段名。</param>
    public void DropIndex(string fieldName)
    {
        ValidateIdentifier(fieldName, nameof(fieldName));
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_drop_index(lease.Ptr, fieldName));
        RefreshSchema();
    }

    /// <summary>优化集合（合并 segment、重建索引等）。</summary>
    public void Optimize()
    {
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_optimize(lease.Ptr));
    }

    // =========================================================================
    // 列 DDL
    // =========================================================================

    /// <summary>
    /// 新增列（已有行按回填表达式求值，空串表示使用默认值）。
    /// <paramref name="fieldSchema"/> 的 <see cref="FieldSchema.IndexParam"/> 非空时随列一并建索引。
    /// 表达式由引擎端求值；非参数化接口——来源不可信时调用方必须自行校验。
    /// </summary>
    public void AddColumn(FieldSchema fieldSchema, string expression = "")
    {
        ArgumentNullException.ThrowIfNull(fieldSchema);
        ArgumentNullException.ThrowIfNull(expression);
        ValidateIdentifier(fieldSchema.Name, nameof(fieldSchema));

        IntPtr nativeField = ParamBuilder.BuildFieldSchema(
            fieldSchema.Name, (uint)fieldSchema.DataType, fieldSchema.Nullable, 0, fieldSchema.IndexParam);
        try
        {
            string? safeExpression = expression.Length == 0 ? null : ValidateExpression(expression, nameof(expression));
            using var lease = AcquireLease();
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_add_column(lease.Ptr, nativeField, safeExpression));
        }
        finally
        {
            NativeMethods.zvec_field_schema_destroy(nativeField);
        }

        RefreshSchema();
    }

    /// <summary>删除列。</summary>
    /// <param name="fieldName">列名。</param>
    public void DropColumn(string fieldName)
    {
        ValidateIdentifier(fieldName, nameof(fieldName));
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_drop_column(lease.Ptr, fieldName));
        RefreshSchema();
    }

    /// <summary>原子性重命名和/或修改列定义（仅数值标量列）。newName 为 null 或空串表示不重命名。</summary>
    public void AlterColumn(string oldName, string? newName = null, FieldSchema? fieldSchema = null)
    {
        ValidateIdentifier(oldName, nameof(oldName));
        // 空串沿用历史契约：等价 null（不重命名），仅拒绝 null 之外的非法值（空/NUL）。
        if (!string.IsNullOrEmpty(newName))
        {
            ValidateIdentifier(newName, nameof(newName));
        }

        IntPtr nativeField = IntPtr.Zero;
        if (fieldSchema is not null)
        {
            ValidateIdentifier(fieldSchema.Name, nameof(fieldSchema));
            nativeField = ParamBuilder.BuildFieldSchema(
                fieldSchema.Name, (uint)fieldSchema.DataType, fieldSchema.Nullable, 0, fieldSchema.IndexParam);
        }

        try
        {
            using var lease = AcquireLease();
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_alter_column(
                lease.Ptr, oldName, string.IsNullOrEmpty(newName) ? null : newName, nativeField));
        }
        finally
        {
            if (nativeField != IntPtr.Zero)
            {
                NativeMethods.zvec_field_schema_destroy(nativeField);
            }
        }

        RefreshSchema();
    }

    private void RefreshSchema()
    {
        // 嵌套租约安全（SafeHandle 引用计数）；DDL 入口已持有租约时此处为计数叠加。
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_get_schema(lease.Ptr, out IntPtr nativeSchema));
        var handle = new DelegateHandle(nativeSchema, NativeMethods.zvec_collection_schema_destroy);
        try
        {
            Schema = ParamBuilder.ReadCollectionSchema(nativeSchema);
        }
        finally
        {
            handle.Dispose();
        }
    }

    // =========================================================================
    // 统计与生命周期
    // =========================================================================

    /// <summary>集合统计（文档数与向量索引完整度）。</summary>
    public CollectionStats Stats
    {
        get
        {
            using var lease = AcquireLease();
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_get_stats(lease.Ptr, out IntPtr stats));
            try
            {
                ulong docCount = NativeMethods.zvec_collection_stats_get_doc_count(stats);
                nuint indexCount = NativeMethods.zvec_collection_stats_get_index_count(stats);
                IndexStat[] indexes = new IndexStat[(int)indexCount];
                for (nuint i = 0; i < indexCount; i++)
                {
                    string name = NativeUtil.PtrToUtf8Required(NativeMethods.zvec_collection_stats_get_index_name(stats, i));
                    float completeness = NativeMethods.zvec_collection_stats_get_index_completeness(stats, i);
                    indexes[(int)i] = new IndexStat(name, completeness);
                }

                return new CollectionStats { DocCount = docCount, Indexes = indexes };
            }
            finally
            {
                NativeMethods.zvec_collection_stats_destroy(stats);
            }
        }
    }

    /// <summary>强制将待写数据落盘。</summary>
    public void Flush()
    {
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_flush(lease.Ptr));
    }

    /// <summary>关闭集合句柄（幂等；等待仍打开的迭代器结束后释放文件锁）。</summary>
    public void Close() => _handle.Dispose();

    /// <summary>永久删除集合及其磁盘数据（不可恢复）。</summary>
    public void Destroy()
    {
        using var lease = AcquireLease();
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_destroy(lease.Ptr));
        // 先标记失效再释放租约引用，避免计数归零时再次触发 close。
        _handle.SetHandleAsInvalid();
    }

    /// <summary>释放集合（等价 <see cref="Close"/>，支持 using）。</summary>
    public void Dispose() => Close();
}
