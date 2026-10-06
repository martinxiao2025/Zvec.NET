namespace Zvec.NET;

/// <summary>
/// 重排器（对齐 Python RerankFunction）：把多路检索结果合并为一份最终排序。
/// RRF 与 Weighted 走引擎内快速路径；Callback 与自定义实现走客户端合并路径。
/// </summary>
public interface IReRanker
{
    /// <summary>合并多路检索结果，返回前 topk 个文档。</summary>
    IReadOnlyList<Doc> Rerank(IReadOnlyList<IReadOnlyList<Doc>> queryResults, int topk);
}

/// <summary>Reciprocal Rank Fusion：score = Σ 1/(k + rank)（对齐 Python RrfReRanker）。</summary>
public sealed class RrfReRanker : IReRanker
{
    public int RankConstant { get; }

    public RrfReRanker(int rankConstant = 60)
    {
        RankConstant = rankConstant;
    }

    public IReadOnlyList<Doc> Rerank(IReadOnlyList<IReadOnlyList<Doc>> queryResults, int topk)
    {
        ArgumentNullException.ThrowIfNull(queryResults);

        var scores = new Dictionary<string, double>();
        var docs = new Dictionary<string, Doc>();
        foreach (IReadOnlyList<Doc> results in queryResults)
        {
            for (int rank = 0; rank < results.Count; rank++)
            {
                Doc doc = results[rank];
                scores[doc.Id] = scores.GetValueOrDefault(doc.Id) + 1.0 / (RankConstant + rank + 1);
                docs[doc.Id] = doc;
            }
        }

        return [.. scores.OrderByDescending(pair => pair.Value)
            .Take(topk)
            .Select(pair =>
            {
                Doc doc = docs[pair.Key];
                doc.Score = (float)pair.Value;
                return doc;
            })];
    }
}

/// <summary>加权重排：每路分数归一化后按权重相加（对齐 Python WeightedReRanker）。</summary>
public sealed class WeightedReRanker : IReRanker
{
    public IReadOnlyList<double> Weights { get; }

    public WeightedReRanker(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        Weights = weights;
    }

    public IReadOnlyList<Doc> Rerank(IReadOnlyList<IReadOnlyList<Doc>> queryResults, int topk)
    {
        ArgumentNullException.ThrowIfNull(queryResults);
        if (Weights.Count != queryResults.Count)
        {
            throw new ArgumentException($"权重数（{Weights.Count}）必须与查询路数（{queryResults.Count}）一致。");
        }

        var scores = new Dictionary<string, double>();
        var docs = new Dictionary<string, Doc>();
        for (int route = 0; route < queryResults.Count; route++)
        {
            IReadOnlyList<Doc> results = queryResults[route];
            if (results.Count == 0)
            {
                continue;
            }

            float max = results.Max(d => d.Score ?? 0f);
            float min = results.Min(d => d.Score ?? 0f);
            float range = max - min;

            for (int rank = 0; rank < results.Count; rank++)
            {
                Doc doc = results[rank];
                float normalized = range > 0 ? ((doc.Score ?? 0f) - min) / range : 1f;
                scores[doc.Id] = scores.GetValueOrDefault(doc.Id) + normalized * Weights[route];
                docs.TryAdd(doc.Id, doc);
            }
        }

        return [.. scores.OrderByDescending(pair => pair.Value)
            .Take(topk)
            .Select(pair =>
            {
                Doc doc = docs[pair.Key];
                doc.Score = (float)pair.Value;
                return doc;
            })];
    }
}

/// <summary>
/// 回调重排：把各路结果交给调用方委托合并（对齐 Python CallbackReRanker；
/// C API 无原生回调入口，始终走客户端合并路径）。
/// </summary>
public sealed class CallbackReRanker : IReRanker
{
    private readonly Func<IReadOnlyList<IReadOnlyList<Doc>>, int, IReadOnlyList<Doc>> _callback;

    public CallbackReRanker(Func<IReadOnlyList<IReadOnlyList<Doc>>, int, IReadOnlyList<Doc>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    public IReadOnlyList<Doc> Rerank(IReadOnlyList<IReadOnlyList<Doc>> queryResults, int topk) =>
        _callback(queryResults, topk);
}
