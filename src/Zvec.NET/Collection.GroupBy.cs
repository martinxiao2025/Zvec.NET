namespace Zvec.NET;

/// <summary>Collection 的分组查询能力。</summary>
public sealed partial class Collection
{
    /// <summary>
    /// 分组向量检索。注意：v0.7.0 C API 未暴露引擎内 group-by 执行入口，
    /// 此实现为客户端模拟——放大 topk 拉取候选后按字段分组截断，超大规模时与引擎语义可能有差异。
    /// </summary>
    public IReadOnlyList<GroupResult> GroupByQuery(Query query, string groupByFieldName, int groupCount = 2,
        int topkPerGroup = 3, string? filter = null, bool includeVector = false,
        IReadOnlyList<string>? outputFields = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentifier(groupByFieldName, nameof(groupByFieldName));
        if (Schema.Field(groupByFieldName) is null)
        {
            throw new ArgumentException($"分组字段 {groupByFieldName} 不在集合 schema 中。", nameof(groupByFieldName));
        }

        List<string>? mergedFields = outputFields is null ? null : [.. outputFields];
        if (mergedFields is not null && !mergedFields.Contains(groupByFieldName))
        {
            mergedFields.Add(groupByFieldName);
        }

        int expandedTopk = Math.Max(topkPerGroup * groupCount * 4, groupCount * topkPerGroup);
        IReadOnlyList<Doc> candidates = Query(query, expandedTopk, ValidateFilter(filter), includeVector, mergedFields);

        var groups = new Dictionary<string, List<Doc>>();
        foreach (Doc doc in candidates)
        {
            string key = doc.Field(groupByFieldName)?.ToString() ?? "<null>";
            if (!groups.TryGetValue(key, out List<Doc>? group))
            {
                if (groups.Count >= groupCount)
                {
                    continue;
                }

                group = [];
                groups[key] = group;
            }

            if (group.Count < topkPerGroup)
            {
                group.Add(doc);
            }
        }

        return [.. groups.Select(pair => new GroupResult { Key = pair.Key, Docs = pair.Value })];
    }
}
