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
        ValidateName(groupByFieldName, nameof(groupByFieldName));
        if (groupCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCount), groupCount, "groupCount 至少为 1。");
        }

        if (topkPerGroup < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(topkPerGroup), topkPerGroup, "topkPerGroup 至少为 1。");
        }

        if (Schema.Field(groupByFieldName) is null)
        {
            throw new ArgumentException($"分组字段 {groupByFieldName} 不在集合 schema 中。", nameof(groupByFieldName));
        }

        List<string>? mergedFields = outputFields is null ? null : [.. outputFields];
        if (mergedFields is not null && !mergedFields.Contains(groupByFieldName))
        {
            mergedFields.Add(groupByFieldName);
        }

        // 候选拉取量放大 4 倍以覆盖分组截断损耗；用 long 累计避免溢出，
        // 越界时抛明确异常而非底层 OverflowException。
        long expandedTopkL = (long)topkPerGroup * groupCount * 4L;
        if (expandedTopkL > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCount),
                $"groupCount={groupCount} 与 topkPerGroup={topkPerGroup} 组合导致候选拉取量 {expandedTopkL} 超过 int.MaxValue，请调小参数。");
        }

        int expandedTopk = (int)expandedTopkL;
        IReadOnlyList<Doc> candidates = Query(query, expandedTopk, filter, includeVector, mergedFields);

        var groups = new Dictionary<string, List<Doc>>();
        foreach (Doc doc in candidates)
        {
            string key = doc.Field(groupByFieldName) is { } fieldValue
                ? Convert.ToString(fieldValue, System.Globalization.CultureInfo.InvariantCulture) ?? ""
                : "<null>";
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
