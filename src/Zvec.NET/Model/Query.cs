namespace Zvec.NET;

/// <summary>全文检索子句（对齐 Python Fts）：query_string 为布尔表达式，match_string 为自然语言匹配串。</summary>
public sealed class Fts
{
    /// <summary>全文布尔查询表达式（如 "machine AND learning"）。</summary>
    public string? QueryString { get; set; }

    /// <summary>自然语言匹配串（按分词结果匹配）。</summary>
    public string? MatchString { get; set; }

    /// <summary>构造全文检索子句。</summary>
    /// <param name="queryString">布尔查询表达式。</param>
    /// <param name="matchString">自然语言匹配串。</param>
    public Fts(string? queryString = null, string? matchString = null)
    {
        QueryString = queryString;
        MatchString = matchString;
    }
}

/// <summary>
/// 单路检索请求（对齐 Python Query）。
/// 向量检索提供 vector 或 id（二选一，id 会先取回该文档向量）；全文检索提供 fts。
/// </summary>
public sealed class Query
{
    /// <summary>目标向量字段名。</summary>
    public string FieldName { get; set; }

    /// <summary>以已有文档 id 作为查询向量来源。</summary>
    public string? Id { get; set; }

    /// <summary>查询向量：float[] / double[] / Half[] / sbyte[] / <see cref="SparseVector"/>。</summary>
    public object? Vector { get; set; }

    /// <summary>索引对应的查询参数（HnswQueryParam 等）。</summary>
    public QueryParam? Param { get; set; }

    /// <summary>全文检索子句。</summary>
    public Fts? Fts { get; set; }

    /// <summary>构造单路检索请求。</summary>
    /// <param name="fieldName">向量字段名。</param>
    /// <param name="id">以文档 id 作为查询向量来源。</param>
    /// <param name="vector">查询向量。</param>
    /// <param name="param">索引查询参数。</param>
    /// <param name="fts">全文检索子句。</param>
    public Query(string fieldName, string? id = null, object? vector = null, QueryParam? param = null, Fts? fts = null)
    {
        FieldName = fieldName;
        Id = id;
        Vector = vector;
        Param = param;
        Fts = fts;
    }

    /// <summary>是否以文档 id 作为查询向量来源。</summary>
    public bool HasId => Id is not null;

    /// <summary>是否携带查询向量。</summary>
    public bool HasVector => Vector is not null;

    /// <summary>是否携带有效的全文检索子句。</summary>
    public bool HasFts => Fts is not null && (Fts.QueryString is not null || Fts.MatchString is not null);
}

/// <summary>打开集合的选项（对齐 Python CollectionOption）。</summary>
public sealed class CollectionOption
{
    /// <summary>以只读模式打开。</summary>
    public bool ReadOnly { get; set; }

    /// <summary>是否启用 mmap（默认启用）。</summary>
    public bool EnableMmap { get; set; } = true;

    /// <summary>最大缓冲字节数；null = 引擎默认。</summary>
    public ulong? MaxBufferSize { get; set; }
}

/// <summary>集合统计信息。</summary>
public sealed class CollectionStats
{
    /// <summary>文档总数。</summary>
    public ulong DocCount { get; init; }

    /// <summary>向量索引列表（标量倒排索引不计入）。</summary>
    public IReadOnlyList<IndexStat> Indexes { get; init; } = [];
}

/// <summary>单个索引的统计项。</summary>
/// <param name="Name">索引字段名。</param>
/// <param name="Completeness">索引构建完整度 [0,1]。</param>
public readonly record struct IndexStat(string Name, float Completeness);

/// <summary>分组检索结果（客户端模拟实现）。</summary>
public sealed class GroupResult
{
    /// <summary>分组键值（标量字段的值）。</summary>
    public object? Key { get; init; }

    /// <summary>该组内按得分排序的文档。</summary>
    public IReadOnlyList<Doc> Docs { get; init; } = [];
}

/// <summary>zvec.init 全局配置（全部可选，语义对齐 Python zvec.init）。</summary>
public sealed class ZvecOptions
{
    /// <summary>日志输出类型；null = 控制台。</summary>
    public LogType? LogType { get; set; }

    /// <summary>日志级别；null = Warn。</summary>
    public LogLevel? LogLevel { get; set; }

    /// <summary>日志目录（仅 LogType.File 使用；默认 "./logs"，父目录需已存在）。</summary>
    public string? LogDir { get; set; } = "./logs";

    /// <summary>日志文件基础名（默认 "zvec.log"）。</summary>
    public string? LogBasename { get; set; } = "zvec.log";

    /// <summary>单日志文件大小上限 MB（默认 2048）。</summary>
    public int? LogFileSize { get; set; } = 2048;

    /// <summary>日志保留天数（默认 7）。</summary>
    public int? LogOverdueDays { get; set; } = 7;

    /// <summary>查询线程数；null = 按 CPU 自动推断。</summary>
    public int? QueryThreads { get; set; }

    /// <summary>后台优化线程数；null = 自动推断。</summary>
    public int? OptimizeThreads { get; set; }

    /// <summary>倒排转正扫阈值 [0,1]（默认 0.9）。</summary>
    public float? InvertToForwardScanRatio { get; set; }

    /// <summary>按主键暴力检索阈值 [0,1]（默认 0.1）。</summary>
    public float? BruteForceByKeysRatio { get; set; }

    /// <summary>FTS 按主键暴力检索阈值 [0,1]（默认 0.05）。</summary>
    public float? FtsBruteForceByKeysRatio { get; set; }

    /// <summary>内存软上限 MB；null = 按容器 cgroup 自动推断（×0.8）。</summary>
    public int? MemoryLimitMb { get; set; }

    /// <summary>jieba 分词词典目录（含 jieba.dict.utf8 与 hmm_model.utf8）。</summary>
    public string? JiebaDictDir { get; set; }
}
