using System.Runtime.InteropServices;

namespace Zvec.NET.Interop;

/// <summary>
/// zvec C API（c_api.h, v0.7.0）的 P/Invoke 绑定。
/// 指针参数统一使用 IntPtr；布尔参数按 C 的 stdbool（1 字节）封送；
/// 字符串一律 UTF-8。绑定函数在首次调用时触发 <see cref="ZvecNative"/> 的库解析。
/// </summary>
internal static unsafe partial class NativeMethods
{
    private const string Lib = "zvec_c_api";

    // =========================================================================
    // 版本与错误
    // =========================================================================

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_get_version();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_check_version(int major, int minor, int patch);

    [LibraryImport(Lib)]
    internal static partial int zvec_get_version_major();

    [LibraryImport(Lib)]
    internal static partial int zvec_get_version_minor();

    [LibraryImport(Lib)]
    internal static partial int zvec_get_version_patch();

    [LibraryImport(Lib)]
    internal static partial int zvec_get_last_error_details(out ZvecErrorDetails error_details);

    [LibraryImport(Lib)]
    internal static partial int zvec_get_last_error(out IntPtr error_msg);

    [LibraryImport(Lib)]
    internal static partial void zvec_clear_error();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_error_code_to_string(int error_code);

    // =========================================================================
    // 全局配置
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_config_log_create_console(int level);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_config_log_create_file(
        int level,
        string dir,
        string basename,
        uint file_size,
        uint overdue_days);

    [LibraryImport(Lib)]
    internal static partial void zvec_config_log_destroy(IntPtr config);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_log_set_level(IntPtr config, int level);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_config_data_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_config_data_destroy(IntPtr config);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_memory_limit(IntPtr config, ulong memory_limit_bytes);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_log_config(IntPtr config, IntPtr log_config);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_query_thread_count(IntPtr config, uint thread_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_optimize_thread_count(IntPtr config, uint thread_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_invert_to_forward_scan_ratio(IntPtr config, float ratio);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_brute_force_by_keys_ratio(IntPtr config, float ratio);

    [LibraryImport(Lib)]
    internal static partial int zvec_config_data_set_fts_brute_force_by_keys_ratio(IntPtr config, float ratio);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_config_data_set_jieba_dict_dir(IntPtr config, string? dir);

    [LibraryImport(Lib)]
    internal static partial int zvec_initialize(IntPtr config);

    [LibraryImport(Lib)]
    internal static partial int zvec_shutdown();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_is_initialized();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void zvec_set_default_jieba_dict_dir(string? dir);

    // =========================================================================
    // 索引参数
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_index_params_create(uint index_type);

    [LibraryImport(Lib)]
    internal static partial void zvec_index_params_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_metric_type(IntPtr parameters, uint metric_type);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_quantize_type(IntPtr parameters, uint quantize_type);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_quantizer_enable_rotate(
        IntPtr parameters,
        [MarshalAs(UnmanagedType.U1)] bool enable_rotate);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_hnsw_params(IntPtr parameters, int m, int ef_construction);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_vamana_params(
        IntPtr parameters,
        int max_degree,
        int search_list_size,
        float alpha,
        [MarshalAs(UnmanagedType.U1)] bool saturate_graph,
        [MarshalAs(UnmanagedType.U1)] bool use_contiguous_memory);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_diskann_params(
        IntPtr parameters, int max_degree, int list_size, int pq_chunk_num);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_ivf_params(
        IntPtr parameters, int n_list, int n_iters, [MarshalAs(UnmanagedType.U1)] bool use_soar);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_ivf_rabitq_params(
        IntPtr parameters, int nlist, int total_bits, int sample_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_index_params_set_invert_params(
        IntPtr parameters,
        [MarshalAs(UnmanagedType.U1)] bool enable_range_opt,
        [MarshalAs(UnmanagedType.U1)] bool enable_wildcard);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_index_params_set_fts_params(
        IntPtr parameters,
        string? tokenizer_name,
        IntPtr filters /* const zvec_string_array_t* */,
        string? extra_params);

    // =========================================================================
    // 查询参数
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_hnsw_create(
        int ef, float radius, [MarshalAs(UnmanagedType.U1)] bool is_linear, [MarshalAs(UnmanagedType.U1)] bool is_using_refiner);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_hnsw_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_ivf_create(
        int nprobe, [MarshalAs(UnmanagedType.U1)] bool is_using_refiner, float scale_factor);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_ivf_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_ivf_rabitq_create(
        int nprobe, float radius, [MarshalAs(UnmanagedType.U1)] bool is_linear, [MarshalAs(UnmanagedType.U1)] bool is_using_refiner);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_ivf_rabitq_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_flat_create(
        [MarshalAs(UnmanagedType.U1)] bool is_using_refiner, float scale_factor);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_flat_destroy(IntPtr parameters);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_query_params_fts_create(string? default_operator);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_fts_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_vamana_create(
        int ef_search, float radius, [MarshalAs(UnmanagedType.U1)] bool is_linear, [MarshalAs(UnmanagedType.U1)] bool is_using_refiner);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_vamana_destroy(IntPtr parameters);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_query_params_diskann_create(int list_size);

    [LibraryImport(Lib)]
    internal static partial void zvec_query_params_diskann_destroy(IntPtr parameters);

    // =========================================================================
    // 向量查询（单路）
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_vector_query_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_vector_query_destroy(IntPtr query);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_topk(IntPtr query, int topk);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_vector_query_set_field_name(IntPtr query, string field_name);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_query_vector(IntPtr query, void* data, nuint size);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_vector_query_set_filter(IntPtr query, string? filter);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_include_vector(IntPtr query, [MarshalAs(UnmanagedType.U1)] bool include);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_include_doc_id(IntPtr query, [MarshalAs(UnmanagedType.U1)] bool include);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_output_fields(IntPtr query, byte** fields, nuint count);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_hnsw_params(IntPtr query, IntPtr hnsw_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_ivf_params(IntPtr query, IntPtr ivf_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_ivf_rabitq_params(IntPtr query, IntPtr ivf_rabitq_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_flat_params(IntPtr query, IntPtr flat_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_fts_params(IntPtr query, IntPtr fts_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_vamana_params(IntPtr query, IntPtr vamana_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_diskann_params(IntPtr query, IntPtr diskann_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_vector_query_set_fts(IntPtr query, IntPtr fts);

    // =========================================================================
    // FTS 载荷
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_fts_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_fts_destroy(IntPtr fts);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_fts_set_query_string(IntPtr fts, string? query_string);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_fts_set_match_string(IntPtr fts, string? match_string);

    // =========================================================================
    // 多路查询 / 子查询
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_multi_query_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_multi_query_destroy(IntPtr query);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_add_sub_query(IntPtr query, IntPtr sub_query);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_set_topk(IntPtr query, int topk);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_multi_query_set_filter(IntPtr query, string? filter);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_set_include_vector(IntPtr query, [MarshalAs(UnmanagedType.U1)] bool include);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_set_output_fields(IntPtr query, byte** fields, nuint count);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_set_rerank_rrf(IntPtr query, int rank_constant);

    [LibraryImport(Lib)]
    internal static partial int zvec_multi_query_set_rerank_weighted(IntPtr query, double* weights, nuint weight_count);

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_sub_query_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_sub_query_destroy(IntPtr query);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_num_candidates(IntPtr query, int num_candidates);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_sub_query_set_field_name(IntPtr query, string field_name);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_query_vector(IntPtr query, void* data, nuint size);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_sparse_vector(IntPtr query, uint* indices, float* values, nuint count);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_hnsw_params(IntPtr query, IntPtr hnsw_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_ivf_params(IntPtr query, IntPtr ivf_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_ivf_rabitq_params(IntPtr query, IntPtr ivf_rabitq_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_flat_params(IntPtr query, IntPtr flat_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_vamana_params(IntPtr query, IntPtr vamana_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_fts_params(IntPtr query, IntPtr fts_params);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_fts(IntPtr query, IntPtr fts);

    [LibraryImport(Lib)]
    internal static partial int zvec_sub_query_set_diskann_params(IntPtr query, IntPtr diskann_params);

    // =========================================================================
    // Collection 选项 / 统计
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_collection_options_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_collection_options_destroy(IntPtr options);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_options_set_enable_mmap(IntPtr options, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_options_set_read_only(IntPtr options, [MarshalAs(UnmanagedType.U1)] bool read_only);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_options_set_max_buffer_size(IntPtr options, nuint size);

    [LibraryImport(Lib)]
    internal static partial ulong zvec_collection_stats_get_doc_count(IntPtr stats);

    [LibraryImport(Lib)]
    internal static partial nuint zvec_collection_stats_get_index_count(IntPtr stats);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_collection_stats_get_index_name(IntPtr stats, nuint index);

    [LibraryImport(Lib)]
    internal static partial float zvec_collection_stats_get_index_completeness(IntPtr stats, nuint index);

    [LibraryImport(Lib)]
    internal static partial void zvec_collection_stats_destroy(IntPtr stats);

    // =========================================================================
    // Field Schema / Collection Schema
    // =========================================================================

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_field_schema_create(
        string name, uint data_type, [MarshalAs(UnmanagedType.U1)] bool nullable, uint dimension);

    [LibraryImport(Lib)]
    internal static partial void zvec_field_schema_destroy(IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial int zvec_field_schema_set_nullable(IntPtr schema, [MarshalAs(UnmanagedType.U1)] bool nullable);

    [LibraryImport(Lib)]
    internal static partial int zvec_field_schema_set_dimension(IntPtr schema, uint dimension);

    [LibraryImport(Lib)]
    internal static partial int zvec_field_schema_set_index_params(IntPtr schema, IntPtr index_params);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_field_schema_get_name(IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial uint zvec_field_schema_get_data_type(IntPtr schema);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_field_schema_is_nullable(IntPtr schema);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_field_schema_is_vector_field(IntPtr schema);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_field_schema_is_sparse_vector(IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial uint zvec_field_schema_get_dimension(IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial uint zvec_field_schema_get_index_type(IntPtr schema);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_collection_schema_create(string name);

    [LibraryImport(Lib)]
    internal static partial void zvec_collection_schema_destroy(IntPtr schema);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_collection_schema_get_name(IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_schema_add_field(IntPtr schema, IntPtr field);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_schema_add_index(IntPtr schema, string field_name, IntPtr index_params);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_schema_drop_index(IntPtr schema, string field_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_collection_schema_has_field(IntPtr schema, string field_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_collection_schema_get_field(IntPtr schema, string field_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_collection_schema_get_vector_field(IntPtr schema, string field_name);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_schema_get_all_field_names(IntPtr schema, out IntPtr names, out nuint count);

    [LibraryImport(Lib)]
    internal static partial void zvec_free(IntPtr ptr);

    [LibraryImport(Lib)]
    internal static partial void zvec_free_str_array(IntPtr array, nuint count);

    // =========================================================================
    // Collection 生命周期与 DDL
    // =========================================================================

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_create_and_open(
        string path, IntPtr schema, IntPtr options, out IntPtr collection);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_open(string path, IntPtr options, out IntPtr collection);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_close(IntPtr collection);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_destroy(IntPtr collection);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_flush(IntPtr collection);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_get_schema(IntPtr collection, out IntPtr schema);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_get_stats(IntPtr collection, out IntPtr stats);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_create_index(IntPtr collection, string field_name, IntPtr index_params);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_drop_index(IntPtr collection, string field_name);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_optimize(IntPtr collection);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_add_column(IntPtr collection, IntPtr field_schema, string? expression);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_drop_column(IntPtr collection, string column_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_alter_column(
        IntPtr collection, string column_name, string? new_name, IntPtr new_schema);

    // =========================================================================
    // DML
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_insert(
        IntPtr collection, IntPtr* docs, nuint doc_count, out nuint success_count, out nuint error_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_insert_with_results(
        IntPtr collection, IntPtr* docs, nuint doc_count, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_update(
        IntPtr collection, IntPtr* docs, nuint doc_count, out nuint success_count, out nuint error_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_update_with_results(
        IntPtr collection, IntPtr* docs, nuint doc_count, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_upsert(
        IntPtr collection, IntPtr* docs, nuint doc_count, out nuint success_count, out nuint error_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_upsert_with_results(
        IntPtr collection, IntPtr* docs, nuint doc_count, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_delete(
        IntPtr collection, byte** pks, nuint pk_count, out nuint success_count, out nuint error_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_delete_with_results(
        IntPtr collection, byte** pks, nuint pk_count, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial void zvec_write_results_free(IntPtr results, nuint result_count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_collection_delete_by_filter(IntPtr collection, string filter);

    // =========================================================================
    // DQL
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_query(
        IntPtr collection, IntPtr query, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_multi_query(
        IntPtr collection, IntPtr query, out IntPtr results, out nuint result_count);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_fetch(
        IntPtr collection,
        byte** primary_keys,
        nuint count,
        byte** output_fields,
        nuint output_field_count,
        [MarshalAs(UnmanagedType.U1)] bool include_vector,
        out IntPtr documents,
        out nuint found_count);

    [LibraryImport(Lib)]
    internal static partial void zvec_docs_free(IntPtr documents, nuint count);

    // =========================================================================
    // 文档迭代器
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_iterator_options_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_iterator_options_destroy(IntPtr options);

    [LibraryImport(Lib)]
    internal static partial int zvec_iterator_options_set_output_fields(IntPtr options, byte** output_fields, nuint count);

    [LibraryImport(Lib)]
    internal static partial int zvec_iterator_options_set_include_vector(IntPtr options, [MarshalAs(UnmanagedType.U1)] bool include);

    [LibraryImport(Lib)]
    internal static partial int zvec_collection_create_iterator(IntPtr collection, IntPtr options, out IntPtr iterator);

    [LibraryImport(Lib)]
    internal static partial int zvec_doc_iterator_next(IntPtr iterator, out IntPtr doc);

    [LibraryImport(Lib)]
    internal static partial void zvec_doc_iterator_close(IntPtr iterator);

    // =========================================================================
    // Doc
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_doc_create();

    [LibraryImport(Lib)]
    internal static partial void zvec_doc_destroy(IntPtr doc);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void zvec_doc_set_pk(IntPtr doc, string pk);

    [LibraryImport(Lib)]
    internal static partial void zvec_doc_set_score(IntPtr doc, float score);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_add_field_by_value(
        IntPtr doc, string field_name, uint data_type, void* value, nuint value_size);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_set_field_null(IntPtr doc, string field_name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr zvec_doc_get_pk_pointer(IntPtr doc);

    [LibraryImport(Lib)]
    internal static partial float zvec_doc_get_score(IntPtr doc);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_get_field_names(IntPtr doc, out IntPtr field_names, out nuint count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_get_field_value_basic(
        IntPtr doc, string field_name, uint field_type, void* value_buffer, nuint buffer_size);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_get_field_value_pointer(
        IntPtr doc, string field_name, uint field_type, out IntPtr value, out nuint value_size);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int zvec_doc_get_field_value_copy(
        IntPtr doc, string field_name, uint field_type, out IntPtr value, out nuint value_size);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool zvec_doc_is_field_null(IntPtr doc, string field_name);

    // =========================================================================
    // 字符串数组辅助（FTS filters 等）
    // =========================================================================

    [LibraryImport(Lib)]
    internal static partial IntPtr zvec_string_array_create(nuint count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void zvec_string_array_add(IntPtr array, nuint idx, string str);

    [LibraryImport(Lib)]
    internal static partial void zvec_string_array_destroy(IntPtr array);
}
