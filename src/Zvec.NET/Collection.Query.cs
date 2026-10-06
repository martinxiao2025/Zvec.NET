using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>Collection 的向量 / 全文检索执行能力。</summary>
public sealed unsafe partial class Collection
{
    /// <summary>向量/全文检索（单路；对齐 Python Collection.query 的单 query 形态）。</summary>
    public IReadOnlyList<Doc> Query(Query? query = null, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null) =>
        QueryCore(query is null ? [] : [query], topk, filter, includeVector, outputFields, reranker);

    /// <summary>多路检索 + 重排（对齐 Python Collection.query 的多 query 形态）。</summary>
    public IReadOnlyList<Doc> Query(IReadOnlyList<Query> queries, int topk = 10, string? filter = null,
        bool includeVector = false, IReadOnlyList<string>? outputFields = null, IReRanker? reranker = null) =>
        QueryCore(queries, topk, filter, includeVector, outputFields, reranker);

    private IReadOnlyList<Doc> QueryCore(IReadOnlyList<Query> queries, int topk, string? filter,
        bool includeVector, IReadOnlyList<string>? outputFields, IReRanker? reranker)
    {
        // 布尔过滤表达式由引擎端求值；此处仅做边界校验（非空/NUL）。
        string? safeFilter = ValidateFilter(filter);

        bool singleDense = queries.Count == 1 && !IsSparseQuery(queries[0]);
        if (singleDense)
        {
            // C API 单路路径：稠密向量 + FTS。
            return ExecuteSingleQuery(queries[0], topk, safeFilter, includeVector, outputFields);
        }

        if (queries.Count == 0)
        {
            // 无查询载荷：纯过滤 / 全表形态。
            return ExecuteSingleQuery(null, topk, safeFilter, includeVector, outputFields);
        }

        // 稀疏单路：C API 的单路查询无法表达稀疏向量（VectorClause 稀疏缓冲无设置入口），
        // 且 MultiQuery 要求至少 2 路 —— 复制为两路相同子查询后用 RRF 合并（结果等价原序）。
        if (queries.Count == 1 && IsSparseQuery(queries[0]))
        {
            return ExecuteMultiQueryNative([queries[0], queries[0]], new RrfReRanker(),
                topk, safeFilter, includeVector, outputFields);
        }

        // 多路 → MultiQuery。
        if (reranker is null)
        {
            throw new ArgumentException("多路查询必须提供 reranker。", nameof(reranker));
        }

        if (reranker is RrfReRanker or WeightedReRanker)
        {
            return ExecuteMultiQueryNative(queries, reranker, topk, safeFilter, includeVector, outputFields);
        }

        // Callback / 自定义 reranker：逐路执行后客户端合并（对齐 Python 行为）。
        List<IReadOnlyList<Doc>> perRoute = [];
        foreach (Query q in queries)
        {
            perRoute.Add(ExecuteSingleOrSparse(q, topk, safeFilter, includeVector, outputFields));
        }

        return reranker.Rerank(perRoute, topk);
    }

    private bool IsSparseQuery(Query query) =>
        query.Vector is SparseVector || (query.HasId && IsSparseVectorField(query.FieldName));

    private bool IsSparseVectorField(string fieldName) =>
        Schema.Vector(fieldName) is { } vector && SchemaUtil.IsSparseVectorDataType(vector.DataType);

    private IReadOnlyList<Doc> ExecuteSingleOrSparse(Query query, int topk, string? safeFilter,
        bool includeVector, IReadOnlyList<string>? outputFields)
    {
        if (!IsSparseQuery(query))
        {
            return ExecuteSingleQuery(query, topk, safeFilter, includeVector, outputFields);
        }

        // 稀疏借道双路 MultiQuery + RRF（等价原序）。
        return ExecuteMultiQueryNative([query, query], new RrfReRanker(), topk, safeFilter, includeVector, outputFields);
    }

    private IReadOnlyList<Doc> ExecuteSingleQuery(Query? query, int topk, string? safeFilter,
        bool includeVector, IReadOnlyList<string>? outputFields)
    {
        IntPtr nativeQuery = NativeMethods.zvec_vector_query_create();
        NativeUtil.ThrowIfNull(nativeQuery, "vector query");
        try
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_topk(nativeQuery, topk));
            NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_include_doc_id(nativeQuery, true));
            NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_include_vector(nativeQuery, includeVector));
            if (safeFilter is not null)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_filter(nativeQuery, safeFilter));
            }

            SetSingleQueryOutputFields(nativeQuery, outputFields);

            if (query is not null)
            {
                ApplyQuery(nativeQuery, query);
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_query(
                Handle, nativeQuery, out IntPtr results, out nuint resultCount));
            return ReadDocArray(results, resultCount);
        }
        finally
        {
            NativeMethods.zvec_vector_query_destroy(nativeQuery);
        }
    }

    private static void SetSingleQueryOutputFields(IntPtr nativeQuery, IReadOnlyList<string>? outputFields)
    {
        if (outputFields is null)
        {
            return;
        }

        using var arena = new NativeArena();
        byte** nativeFields = arena.AllocUtf8Array([.. outputFields], out nuint count);
        NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_output_fields(nativeQuery, nativeFields, count));
    }

    /// <summary>在单路查询对象上应用 Query（field/参数/FTS/向量）。</summary>
    private void ApplyQuery(IntPtr nativeQuery, Query query)
    {
        NativeUtil.ThrowIfError(NativeMethods.zvec_vector_query_set_field_name(
            nativeQuery, ValidateIdentifier(query.FieldName, "FieldName")));
        ParamBuilder.ApplyQueryParam(nativeQuery, query.Param, isSubQuery: false);

        if (query.Fts is not null)
        {
            ApplyFts(nativeQuery, query.Fts, isSubQuery: false);
        }

        object? vectorValue = query.Vector;
        if (vectorValue is null && query.HasId)
        {
            vectorValue = ResolveVectorById(query.Id!, query.FieldName);
        }

        if (vectorValue is not null)
        {
            if (vectorValue is SparseVector)
            {
                throw new NotSupportedException("稀疏向量请使用多路查询路径（Query 列表）。");
            }

            SetDenseQueryVector(NativeMethods.zvec_vector_query_set_query_vector, nativeQuery, vectorValue, query.FieldName);
        }
    }

    private object ResolveVectorById(string id, string fieldName)
    {
        Doc doc = FetchInternal([id], includeVector: true, outputFields: [fieldName])[id]
            ?? throw new ArgumentException($"文档 {id} 不存在，无法以其作为查询向量来源。", nameof(id));
        return doc.Vectors[fieldName]
            ?? throw new ArgumentException($"文档 {id} 缺少向量字段 {fieldName}。", nameof(id));
    }

    private delegate int SetVectorDelegate(IntPtr query, void* data, nuint size);

    private void SetDenseQueryVector(SetVectorDelegate setter, IntPtr nativeQuery, object vectorValue, string fieldName)
    {
        byte[] encoded = DocCodec.EncodeDenseVectorBytes(vectorValue, GetVectorDataType(fieldName));
        fixed (byte* p = encoded)
        {
            NativeUtil.ThrowIfError(setter(nativeQuery, p, (nuint)encoded.Length));
        }
    }

    private DataType GetVectorDataType(string fieldName) =>
        Schema.Vector(fieldName)?.DataType
        ?? throw new ArgumentException($"向量字段 {fieldName} 不在集合 schema 中。", nameof(fieldName));

    private static void ApplyFts(IntPtr nativeQuery, Fts fts, bool isSubQuery)
    {
        IntPtr nativeFts = NativeMethods.zvec_fts_create();
        NativeUtil.ThrowIfNull(nativeFts, "fts");
        try
        {
            if (fts.QueryString is not null)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_fts_set_query_string(nativeFts, fts.QueryString));
            }

            if (fts.MatchString is not null)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_fts_set_match_string(nativeFts, fts.MatchString));
            }

            int err = isSubQuery
                ? NativeMethods.zvec_sub_query_set_fts(nativeQuery, nativeFts)
                : NativeMethods.zvec_vector_query_set_fts(nativeQuery, nativeFts);
            NativeUtil.ThrowIfError(err);
        }
        finally
        {
            NativeMethods.zvec_fts_destroy(nativeFts);
        }
    }

    /// <summary>原生 MultiQuery 快速路径（RRF / Weighted 重排在引擎内完成）。</summary>
    private IReadOnlyList<Doc> ExecuteMultiQueryNative(IReadOnlyList<Query> queries, IReRanker reranker,
        int topk, string? safeFilter, bool includeVector, IReadOnlyList<string>? outputFields)
    {
        IntPtr multiQuery = NativeMethods.zvec_multi_query_create();
        NativeUtil.ThrowIfNull(multiQuery, "multi query");
        try
        {
            foreach (Query query in queries)
            {
                AddSubQuery(multiQuery, query, topk);
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_topk(multiQuery, topk));
            NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_include_vector(multiQuery, includeVector));
            if (safeFilter is not null)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_filter(multiQuery, safeFilter));
            }

            SetMultiQueryOutputFields(multiQuery, outputFields);

            switch (reranker)
            {
                case RrfReRanker rrf:
                    NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_rerank_rrf(multiQuery, rrf.RankConstant));
                    break;
                case WeightedReRanker weighted:
                    if (weighted.Weights.Count != queries.Count)
                    {
                        throw new ArgumentException(
                            $"WeightedReRanker 的权重数（{weighted.Weights.Count}）必须与查询数（{queries.Count}）一致。");
                    }

                    double[] weights = [.. weighted.Weights];
                    fixed (double* weightsPtr = weights)
                    {
                        NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_rerank_weighted(
                            multiQuery, weightsPtr, (nuint)weights.Length));
                    }

                    break;
                default:
                    throw new NotSupportedException("原生快速路径仅支持 RRF 与 Weighted 重排。");
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_multi_query(
                Handle, multiQuery, out IntPtr results, out nuint resultCount));
            return ReadDocArray(results, resultCount);
        }
        finally
        {
            NativeMethods.zvec_multi_query_destroy(multiQuery);
        }
    }

    private static void SetMultiQueryOutputFields(IntPtr multiQuery, IReadOnlyList<string>? outputFields)
    {
        if (outputFields is null)
        {
            return;
        }

        using var arena = new NativeArena();
        byte** nativeFields = arena.AllocUtf8Array([.. outputFields], out nuint count);
        NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_set_output_fields(multiQuery, nativeFields, count));
    }

    private void AddSubQuery(IntPtr multiQuery, Query query, int topk)
    {
        IntPtr subQuery = NativeMethods.zvec_sub_query_create();
        NativeUtil.ThrowIfNull(subQuery, "sub query");
        try
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_sub_query_set_num_candidates(subQuery, Math.Max(topk, 10)));
            NativeUtil.ThrowIfError(NativeMethods.zvec_sub_query_set_field_name(
                subQuery, ValidateIdentifier(query.FieldName, "FieldName")));
            ParamBuilder.ApplyQueryParam(subQuery, query.Param, isSubQuery: true);

            if (query.Fts is not null)
            {
                ApplyFts(subQuery, query.Fts, isSubQuery: true);
            }

            object? vectorValue = query.Vector;
            if (vectorValue is null && query.HasId)
            {
                vectorValue = ResolveVectorById(query.Id!, query.FieldName);
            }

            if (vectorValue is SparseVector sparse)
            {
                fixed (uint* indices = sparse.Indices)
                fixed (float* values = sparse.Values)
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_sub_query_set_sparse_vector(
                        subQuery, indices, values, (nuint)sparse.Count));
                }
            }
            else if (vectorValue is not null)
            {
                SetDenseQueryVector(NativeMethods.zvec_sub_query_set_query_vector, subQuery, vectorValue, query.FieldName);
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_multi_query_add_sub_query(multiQuery, subQuery));
        }
        finally
        {
            NativeMethods.zvec_sub_query_destroy(subQuery);
        }
    }

    private IReadOnlyList<Doc> ReadDocArray(IntPtr results, nuint count)
    {
        try
        {
            var docs = new IntPtr[(int)count];
            Marshal.Copy(results, docs, 0, (int)count);
            Doc[] output = new Doc[(int)count];
            for (int i = 0; i < docs.Length; i++)
            {
                output[i] = DocCodec.ReadDoc(docs[i], Schema);
            }

            return output;
        }
        finally
        {
            NativeMethods.zvec_docs_free(results, count);
        }
    }
}
