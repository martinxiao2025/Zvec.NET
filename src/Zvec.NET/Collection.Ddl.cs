using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的 DDL、统计与生命周期管理。</summary>
public sealed partial class Collection
{
    // =========================================================================
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
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_create_index(Handle, fieldName, nativeParams));
        }
        finally
        {
            NativeMethods.zvec_index_params_destroy(nativeParams);
        }

        RefreshSchema();
    }

    public void DropIndex(string fieldName)
    {
        ValidateIdentifier(fieldName, nameof(fieldName));
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_drop_index(Handle, fieldName));
        RefreshSchema();
    }

    /// <summary>优化集合（合并 segment、重建索引等）。</summary>
    public void Optimize() => NativeUtil.ThrowIfError(NativeMethods.zvec_collection_optimize(Handle));

    // =========================================================================
    // 列 DDL
    // =========================================================================

    /// <summary>
    /// 新增列（已有行按回填表达式求值，空串表示使用默认值）。
    /// 表达式由引擎端求值；非参数化接口——来源不可信时调用方必须自行校验。
    /// </summary>
    public void AddColumn(FieldSchema fieldSchema, string expression = "")
    {
        ArgumentNullException.ThrowIfNull(fieldSchema);

        IntPtr nativeField = ParamBuilder.BuildFieldSchema(
            fieldSchema.Name, (uint)fieldSchema.DataType, fieldSchema.Nullable, 0);
        try
        {
            string? safeExpression = expression.Length == 0 ? null : ValidateExpression(expression, nameof(expression));
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_add_column(Handle, nativeField, safeExpression));
        }
        finally
        {
            NativeMethods.zvec_field_schema_destroy(nativeField);
        }

        RefreshSchema();
    }

    public void DropColumn(string fieldName)
    {
        ValidateIdentifier(fieldName, nameof(fieldName));
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_drop_column(Handle, fieldName));
        RefreshSchema();
    }

    /// <summary>原子性重命名和/或修改列定义（仅数值标量列）。</summary>
    public void AlterColumn(string oldName, string? newName = null, FieldSchema? fieldSchema = null)
    {
        ValidateIdentifier(oldName, nameof(oldName));

        IntPtr nativeField = IntPtr.Zero;
        if (fieldSchema is not null)
        {
            nativeField = ParamBuilder.BuildFieldSchema(
                fieldSchema.Name, (uint)fieldSchema.DataType, fieldSchema.Nullable, 0);
        }

        try
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_alter_column(
                Handle, oldName, string.IsNullOrEmpty(newName) ? null : newName, nativeField));
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
        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_get_schema(HandleNoAddRef, out IntPtr nativeSchema));
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

    public CollectionStats Stats
    {
        get
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_get_stats(HandleNoAddRef, out IntPtr stats));
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
    public void Flush() => NativeUtil.ThrowIfError(NativeMethods.zvec_collection_flush(Handle));

    /// <summary>关闭集合句柄（幂等；等待仍打开的迭代器结束后释放文件锁）。</summary>
    public void Close() => _handle.Dispose();

    /// <summary>永久删除集合及其磁盘数据（不可恢复）。</summary>
    public void Destroy()
    {
        IntPtr handle = Handle;
        try
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_destroy(handle));
        }
        finally
        {
            _handle.SetHandleAsInvalid();
        }
    }

    public void Dispose() => Close();
}
