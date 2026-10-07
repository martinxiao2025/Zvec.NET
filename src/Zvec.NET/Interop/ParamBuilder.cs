using System.Runtime.InteropServices;

namespace Zvec.NET.Interop;

/// <summary>托管模型 → 原生对象的构建器。所有返回的指针均由调用方拥有，除非显式转移。</summary>
internal static unsafe class ParamBuilder
{
    /// <summary>IVF/Flat refiner 候选扩展倍数的引擎默认值（与 QueryParams 中属性默认一致）。</summary>
    private const float ScaleFactorDefault = 10f;

    /// <summary>构建原生索引参数（调用方拥有；用于 set 后必须 destroy 的 API——schema_set_index_params / collection_create_index 均为深拷贝语义）。</summary>
    internal static IntPtr BuildIndexParam(IndexParam param)
    {
        uint indexType = param.Type switch
        {
            IndexType.Hnsw => NativeTypes.IndexTypeHnsw,
            IndexType.HnswRabitq => NativeTypes.IndexTypeHnswRabitq,
            IndexType.Ivf => NativeTypes.IndexTypeIvf,
            IndexType.IvfRabitq => NativeTypes.IndexTypeIvfRabitq,
            IndexType.Flat => NativeTypes.IndexTypeFlat,
            IndexType.DiskAnn => NativeTypes.IndexTypeDiskAnn,
            IndexType.Vamana => NativeTypes.IndexTypeVamana,
            IndexType.Invert => NativeTypes.IndexTypeInvert,
            IndexType.Fts => NativeTypes.IndexTypeFts,
            _ => throw new NotSupportedException($"不支持的索引类型 {param.Type}"),
        };

        IntPtr handle = NativeMethods.zvec_index_params_create(indexType);
        NativeUtil.ThrowIfNull(handle, $"index params ({param.Type})");
        try
        {
            if (param is VectorIndexParam vectorParam)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_metric_type(handle, (uint)vectorParam.MetricType));
                NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_quantize_type(handle, (uint)vectorParam.QuantizeType));
                NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_quantizer_enable_rotate(handle, vectorParam.QuantizerParam.EnableRotate));
            }

            switch (param)
            {
                case HnswIndexParam hnsw:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_hnsw_params(handle, hnsw.M, hnsw.EfConstruction));
                    break;
                case IvfIndexParam ivf:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_ivf_params(handle, ivf.NList, ivf.NIters, ivf.UseSoar));
                    break;
                case IvfRabitqIndexParam ivfRabitq:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_ivf_rabitq_params(handle, ivfRabitq.NList, ivfRabitq.TotalBits, ivfRabitq.SampleCount));
                    break;
                case DiskAnnIndexParam diskAnn:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_diskann_params(handle, diskAnn.MaxDegree, diskAnn.ListSize, diskAnn.PqChunkNum));
                    break;
                case VamanaIndexParam vamana:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_vamana_params(
                        handle, vamana.MaxDegree, vamana.SearchListSize, vamana.Alpha, vamana.SaturateGraph, vamana.UseContiguousMemory));
                    break;
                case InvertIndexParam invert:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_invert_params(handle, invert.EnableRangeOptimization, invert.EnableExtendedWildcard));
                    break;
                case FtsIndexParam fts:
                    BuildFtsIndexParams(handle, fts);
                    break;
            }

            IntPtr result = handle;
            handle = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.zvec_index_params_destroy(handle);
            }
        }
    }

    private static void BuildFtsIndexParams(IntPtr handle, FtsIndexParam fts)
    {
        IntPtr filterArray = NativeMethods.zvec_string_array_create((nuint)fts.Filters.Count);
        NativeUtil.ThrowIfNull(filterArray, "fts filters");
        try
        {
            for (int i = 0; i < fts.Filters.Count; i++)
            {
                NativeMethods.zvec_string_array_add(filterArray, (nuint)i, fts.Filters[i]);
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_index_params_set_fts_params(
                handle, fts.TokenizerName, filterArray, fts.ExtraParams.Length == 0 ? null : fts.ExtraParams));
        }
        finally
        {
            NativeMethods.zvec_string_array_destroy(filterArray);
        }
    }

    /// <summary>构建原生查询参数（调用方拥有；set_xxx_params 成功后所有权转移给查询对象）。</summary>
    internal static IntPtr BuildQueryParam(QueryParam param) => param switch
    {
        HnswQueryParam p => NativeMethods.zvec_query_params_hnsw_create(p.Ef, p.Radius, p.IsLinear, p.IsUsingRefiner),
        HnswRabitqQueryParam p => NativeMethods.zvec_query_params_hnsw_create(p.Ef, p.Radius, p.IsLinear, p.IsUsingRefiner),
        IvfQueryParam p => NativeMethods.zvec_query_params_ivf_create(p.NProbe, p.IsUsingRefiner, ScaleFactorDefault),
        IvfRabitqQueryParam p => NativeMethods.zvec_query_params_ivf_rabitq_create(p.NProbe, p.Radius, p.IsLinear, p.IsUsingRefiner),
        FlatQueryParam p => NativeMethods.zvec_query_params_flat_create(p.IsUsingRefiner, p.ScaleFactor),
        VamanaQueryParam p => NativeMethods.zvec_query_params_vamana_create(p.EfSearch, p.Radius, p.IsLinear, p.IsUsingRefiner),
        DiskAnnQueryParam p => NativeMethods.zvec_query_params_diskann_create(p.ListSize),
        FtsQueryParam p => NativeMethods.zvec_query_params_fts_create(
            string.IsNullOrEmpty(p.DefaultOperator) ? null : p.DefaultOperator),
        _ => throw new NotSupportedException($"不支持的查询参数 {param.GetType().Name}"),
    };

    /// <summary>查询对象上的 set_xxx_params 分发（成功后原生查询对象获得参数所有权，本方法负责失败路径销毁）。</summary>
    internal static void ApplyQueryParam(IntPtr query, QueryParam? param, bool isSubQuery)
    {
        if (param is null)
        {
            return;
        }

        IntPtr nativeParam = BuildQueryParam(param);
        NativeUtil.ThrowIfNull(nativeParam, $"query params ({param.GetType().Name})");
        try
        {
            int err = (param, isSubQuery) switch
            {
                (HnswQueryParam _, false) => NativeMethods.zvec_vector_query_set_hnsw_params(query, nativeParam),
                (HnswQueryParam _, true) => NativeMethods.zvec_sub_query_set_hnsw_params(query, nativeParam),
                (HnswRabitqQueryParam _, false) => NativeMethods.zvec_vector_query_set_hnsw_params(query, nativeParam),
                (HnswRabitqQueryParam _, true) => NativeMethods.zvec_sub_query_set_hnsw_params(query, nativeParam),
                (IvfQueryParam _, false) => NativeMethods.zvec_vector_query_set_ivf_params(query, nativeParam),
                (IvfQueryParam _, true) => NativeMethods.zvec_sub_query_set_ivf_params(query, nativeParam),
                (IvfRabitqQueryParam _, false) => NativeMethods.zvec_vector_query_set_ivf_rabitq_params(query, nativeParam),
                (IvfRabitqQueryParam _, true) => NativeMethods.zvec_sub_query_set_ivf_rabitq_params(query, nativeParam),
                (FlatQueryParam _, false) => NativeMethods.zvec_vector_query_set_flat_params(query, nativeParam),
                (FlatQueryParam _, true) => NativeMethods.zvec_sub_query_set_flat_params(query, nativeParam),
                (VamanaQueryParam _, false) => NativeMethods.zvec_vector_query_set_vamana_params(query, nativeParam),
                (VamanaQueryParam _, true) => NativeMethods.zvec_sub_query_set_vamana_params(query, nativeParam),
                (DiskAnnQueryParam _, false) => NativeMethods.zvec_vector_query_set_diskann_params(query, nativeParam),
                (DiskAnnQueryParam _, true) => NativeMethods.zvec_sub_query_set_diskann_params(query, nativeParam),
                (FtsQueryParam _, false) => NativeMethods.zvec_vector_query_set_fts_params(query, nativeParam),
                (FtsQueryParam _, true) => NativeMethods.zvec_sub_query_set_fts_params(query, nativeParam),
                _ => throw new NotSupportedException($"不支持的查询参数 {param.GetType().Name}"),
            };
            NativeUtil.ThrowIfError(err);

            // 所有权已转移，防止 finally 销毁。
            nativeParam = IntPtr.Zero;
        }
        finally
        {
            if (nativeParam != IntPtr.Zero)
            {
                DestroyQueryParam(param, nativeParam);
            }
        }
    }

    private static void DestroyQueryParam(QueryParam param, IntPtr nativeParam)
    {
        switch (param)
        {
            case HnswQueryParam:
            case HnswRabitqQueryParam:
                NativeMethods.zvec_query_params_hnsw_destroy(nativeParam);
                break;
            case IvfQueryParam:
                NativeMethods.zvec_query_params_ivf_destroy(nativeParam);
                break;
            case IvfRabitqQueryParam:
                NativeMethods.zvec_query_params_ivf_rabitq_destroy(nativeParam);
                break;
            case FlatQueryParam:
                NativeMethods.zvec_query_params_flat_destroy(nativeParam);
                break;
            case VamanaQueryParam:
                NativeMethods.zvec_query_params_vamana_destroy(nativeParam);
                break;
            case DiskAnnQueryParam:
                NativeMethods.zvec_query_params_diskann_destroy(nativeParam);
                break;
            case FtsQueryParam:
                NativeMethods.zvec_query_params_fts_destroy(nativeParam);
                break;
        }
    }

    /// <summary>构建原生 CollectionSchema（调用方拥有；create_and_open 后需 destroy）。</summary>
    internal static IntPtr BuildCollectionSchema(CollectionSchema schema)
    {
        IntPtr nativeSchema = NativeMethods.zvec_collection_schema_create(schema.Name);
        NativeUtil.ThrowIfNull(nativeSchema, "collection schema");
        try
        {
            void AddField(string name, uint dataType, bool nullable, uint dimension, IndexParam? indexParam)
            {
                IntPtr nativeField = BuildFieldSchema(name, dataType, nullable, dimension);
                try
                {
                    if (indexParam is not null)
                    {
                        IntPtr indexParams = BuildIndexParam(indexParam);
                        try
                        {
                            NativeUtil.ThrowIfError(NativeMethods.zvec_field_schema_set_index_params(nativeField, indexParams));
                        }
                        finally
                        {
                            NativeMethods.zvec_index_params_destroy(indexParams);
                        }
                    }

                    NativeUtil.ThrowIfError(NativeMethods.zvec_collection_schema_add_field(nativeSchema, nativeField));
                }
                finally
                {
                    NativeMethods.zvec_field_schema_destroy(nativeField);
                }
            }

            foreach (FieldSchema field in schema.Fields)
            {
                AddField(field.Name, (uint)field.DataType, field.Nullable, dimension: 0, field.IndexParam);
            }

            foreach (VectorSchema vector in schema.Vectors)
            {
                AddField(vector.Name, (uint)vector.DataType, vector.Nullable, vector.Dimension, vector.IndexParam);
            }

            IntPtr result = nativeSchema;
            nativeSchema = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (nativeSchema != IntPtr.Zero)
            {
                NativeMethods.zvec_collection_schema_destroy(nativeSchema);
            }
        }
    }

    internal static IntPtr BuildFieldSchema(string name, uint dataType, bool nullable, uint dimension)
    {
        IntPtr field = NativeMethods.zvec_field_schema_create(name, dataType, nullable, dimension);
        NativeUtil.ThrowIfNull(field, $"field schema {name}");
        return field;
    }

    /// <summary>从原生 CollectionSchema 读取托管镜像（nativeSchema 必须保持存活）。</summary>
    internal static CollectionSchema ReadCollectionSchema(IntPtr nativeSchema)
    {
        string name = NativeUtil.PtrToUtf8Required(NativeMethods.zvec_collection_schema_get_name(nativeSchema));
        var schema = new CollectionSchema(name);

        NativeUtil.ThrowIfError(NativeMethods.zvec_collection_schema_get_all_field_names(
            nativeSchema, out IntPtr namesPtr, out nuint count));
        try
        {
            var names = new IntPtr[(int)count];
            Marshal.Copy(namesPtr, names, 0, (int)count);

            foreach (IntPtr namePtr in names)
            {
                string fieldName = NativeUtil.PtrToUtf8Required(namePtr);
                IntPtr field = NativeMethods.zvec_collection_schema_get_field(nativeSchema, fieldName);
                if (field == IntPtr.Zero)
                {
                    continue;
                }

                bool isVector = NativeMethods.zvec_field_schema_is_vector_field(field);
                uint dataType = NativeMethods.zvec_field_schema_get_data_type(field);
                bool nullable = NativeMethods.zvec_field_schema_is_nullable(field);
                uint dimension = NativeMethods.zvec_field_schema_get_dimension(field);
                uint indexType = NativeMethods.zvec_field_schema_get_index_type(field);

                if (isVector)
                {
                    var vector = new VectorSchema(fieldName, (DataType)dataType, dimension, nullable)
                    {
                        IndexParam = indexType == NativeTypes.IndexTypeUndefined ? null : new OpaqueIndexParam((IndexType)indexType),
                    };
                    schema.AddVector(vector);
                }
                else
                {
                    var fieldSchema = new FieldSchema(fieldName, (DataType)dataType, nullable)
                    {
                        IndexParam = indexType == NativeTypes.IndexTypeUndefined ? null : new OpaqueIndexParam((IndexType)indexType),
                    };
                    schema.AddField(fieldSchema);
                }
            }
        }
        finally
        {
            NativeMethods.zvec_free(namesPtr);
        }

        return schema;
    }

    /// <summary>占位索引参数：schema 读回时仅携带类型信息。</summary>
    private sealed class OpaqueIndexParam(IndexType type) : IndexParam
    {
        public override IndexType Type { get; } = type;
    }

    internal static IntPtr BuildCollectionOptions(CollectionOption? option)
    {
        IntPtr native = NativeMethods.zvec_collection_options_create();
        NativeUtil.ThrowIfNull(native, "collection options");
        try
        {
            if (option is not null)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_collection_options_set_read_only(native, option.ReadOnly));
                NativeUtil.ThrowIfError(NativeMethods.zvec_collection_options_set_enable_mmap(native, option.EnableMmap));
                if (option.MaxBufferSize is { } maxSize)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_collection_options_set_max_buffer_size(native, (nuint)maxSize));
                }
            }

            IntPtr result = native;
            native = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (native != IntPtr.Zero)
            {
                NativeMethods.zvec_collection_options_destroy(native);
            }
        }
    }
}
