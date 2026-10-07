namespace Zvec.NET;

/// <summary>量化器附加参数。</summary>
public sealed class QuantizerParam
{
    /// <summary>INT8/INT4 量化前是否施加随机旋转以降低量化误差。</summary>
    public bool EnableRotate { get; set; }
}

/// <summary>索引参数基类。</summary>
public abstract class IndexParam
{
    /// <summary>索引类型。</summary>
    public abstract IndexType Type { get; }
}

/// <summary>向量索引参数基类。</summary>
public abstract class VectorIndexParam : IndexParam
{
    /// <summary>距离度量（默认 Undefined = 引擎按索引默认值）。</summary>
    public MetricType MetricType { get; set; } = MetricType.Undefined;

    /// <summary>量化类型（默认不量化）。</summary>
    public QuantizeType QuantizeType { get; set; } = QuantizeType.Undefined;

    /// <summary>量化器附加参数。</summary>
    public QuantizerParam QuantizerParam { get; set; } = new();
}

/// <summary>Flat 精确检索索引（默认 IP 度量）。</summary>
public sealed class FlatIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Flat;

    /// <summary>以度量类型与量化类型构造 Flat 索引参数。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="quantizeType">量化类型。</param>
    public FlatIndexParam(MetricType metricType = MetricType.Ip, QuantizeType quantizeType = QuantizeType.Undefined)
    {
        MetricType = metricType;
        QuantizeType = quantizeType;
    }
}

/// <summary>HNSW 图索引。</summary>
public sealed class HnswIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Hnsw;

    /// <summary>每个节点的双向连接数（默认 50）。</summary>
    public int M { get; set; } = 50;

    /// <summary>建图时候选列表大小（默认 500）。</summary>
    public int EfConstruction { get; set; } = 500;

    /// <summary>是否使用连续内存布局。
    /// 注意：v0.7.0 C API 的 HNSW 参数入口未暴露该开关（仅 Vamana 支持），此属性对 HNSW 索引当前不生效。</summary>
    public bool UseContiguousMemory { get; set; }

    /// <summary>构造 HNSW 索引参数。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="m">每个节点的双向连接数。</param>
    /// <param name="efConstruction">建图时候选列表大小。</param>
    /// <param name="quantizeType">量化类型。</param>
    /// <param name="useContiguousMemory">是否连续内存布局（当前版本对 HNSW 不生效）。</param>
    public HnswIndexParam(MetricType metricType = MetricType.Ip, int m = 50, int efConstruction = 500,
        QuantizeType quantizeType = QuantizeType.Undefined, bool useContiguousMemory = false)
    {
        MetricType = metricType;
        M = m;
        EfConstruction = efConstruction;
        QuantizeType = quantizeType;
        UseContiguousMemory = useContiguousMemory;
    }
}

/// <summary>
/// HNSW + RaBitQ 量化索引。
/// 注意：v0.7.0 C API 仅暴露 metric/quantize 设置；m、ef_construction、total_bits 等使用引擎默认值。
/// </summary>
public sealed class HnswRabitqIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.HnswRabitq;

    /// <summary>构造 HNSW RaBitQ 索引参数（量化固定为 RaBitQ）。</summary>
    /// <param name="metricType">距离度量。</param>
    public HnswRabitqIndexParam(MetricType metricType = MetricType.Ip)
    {
        MetricType = metricType;
        QuantizeType = QuantizeType.Rabitq;
    }
}

/// <summary>IVF 倒排文件索引。</summary>
public sealed class IvfIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Ivf;

    /// <summary>聚类中心数（默认 10）。</summary>
    public int NList { get; set; } = 10;

    /// <summary>k-means 迭代次数（默认 10）。</summary>
    public int NIters { get; set; } = 10;

    /// <summary>是否启用 SOAR 优化。</summary>
    public bool UseSoar { get; set; }

    /// <summary>构造 IVF 索引参数。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="nList">聚类中心数。</param>
    /// <param name="nIters">k-means 迭代次数。</param>
    /// <param name="useSoar">是否启用 SOAR 优化。</param>
    /// <param name="quantizeType">量化类型。</param>
    public IvfIndexParam(MetricType metricType = MetricType.Ip, int nList = 10, int nIters = 10,
        bool useSoar = false, QuantizeType quantizeType = QuantizeType.Undefined)
    {
        MetricType = metricType;
        NList = nList;
        NIters = nIters;
        UseSoar = useSoar;
        QuantizeType = quantizeType;
    }
}

/// <summary>IVF + RaBitQ 量化索引。</summary>
public sealed class IvfRabitqIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.IvfRabitq;

    /// <summary>聚类中心数（默认 1024）。</summary>
    public int NList { get; set; } = 1024;

    /// <summary>RaBitQ 量化总位数（默认 7）。</summary>
    public int TotalBits { get; set; } = 7;

    /// <summary>训练采样数，0 表示使用全部向量。</summary>
    public int SampleCount { get; set; }

    /// <summary>构造 IVF RaBitQ 索引参数（量化固定为 RaBitQ）。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="nList">聚类中心数。</param>
    /// <param name="totalBits">RaBitQ 量化总位数。</param>
    /// <param name="sampleCount">训练采样数。</param>
    public IvfRabitqIndexParam(MetricType metricType = MetricType.Ip, int nList = 1024, int totalBits = 7, int sampleCount = 0)
    {
        MetricType = metricType;
        NList = nList;
        TotalBits = totalBits;
        SampleCount = sampleCount;
        QuantizeType = QuantizeType.Rabitq;
    }
}

/// <summary>DiskANN 磁盘索引。</summary>
public sealed class DiskAnnIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.DiskAnn;

    /// <summary>图最大度，范围 [1,100]（默认 100）。</summary>
    public int MaxDegree { get; set; } = 100;

    /// <summary>建图候选列表大小，范围 [10,100]（默认 50）。</summary>
    public int ListSize { get; set; } = 50;

    /// <summary>PQ 分块数，范围 [1,1024]，0 = 自动（默认）。</summary>
    public int PqChunkNum { get; set; }

    /// <summary>构造 DiskANN 索引参数。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="maxDegree">图最大度。</param>
    /// <param name="listSize">建图候选列表大小。</param>
    /// <param name="pqChunkNum">PQ 分块数。</param>
    /// <param name="quantizeType">量化类型。</param>
    public DiskAnnIndexParam(MetricType metricType = MetricType.Ip, int maxDegree = 100, int listSize = 50,
        int pqChunkNum = 0, QuantizeType quantizeType = QuantizeType.Undefined)
    {
        MetricType = metricType;
        MaxDegree = maxDegree;
        ListSize = listSize;
        PqChunkNum = pqChunkNum;
        QuantizeType = quantizeType;
    }
}

/// <summary>
/// Vamana 图索引（C API 额外能力，Python SDK 未暴露）。
/// </summary>
public sealed class VamanaIndexParam : VectorIndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Vamana;

    /// <summary>图最大度（默认 64）。</summary>
    public int MaxDegree { get; set; } = 64;

    /// <summary>建图候选列表大小（默认 100）。</summary>
    public int SearchListSize { get; set; } = 100;

    /// <summary>剪枝松紧度（默认 1.2）。</summary>
    public float Alpha { get; set; } = 1.2f;

    /// <summary>是否饱和剪枝。</summary>
    public bool SaturateGraph { get; set; }

    /// <summary>是否使用连续内存布局。</summary>
    public bool UseContiguousMemory { get; set; }

    /// <summary>构造 Vamana 索引参数。</summary>
    /// <param name="metricType">距离度量。</param>
    /// <param name="maxDegree">图最大度。</param>
    /// <param name="searchListSize">建图候选列表大小。</param>
    /// <param name="alpha">剪枝松紧度。</param>
    /// <param name="saturateGraph">是否饱和剪枝。</param>
    /// <param name="useContiguousMemory">是否连续内存布局。</param>
    public VamanaIndexParam(MetricType metricType = MetricType.Ip, int maxDegree = 64, int searchListSize = 100,
        float alpha = 1.2f, bool saturateGraph = false, bool useContiguousMemory = false)
    {
        MetricType = metricType;
        MaxDegree = maxDegree;
        SearchListSize = searchListSize;
        Alpha = alpha;
        SaturateGraph = saturateGraph;
        UseContiguousMemory = useContiguousMemory;
    }
}

/// <summary>标量字段倒排索引。</summary>
public sealed class InvertIndexParam : IndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Invert;

    /// <summary>是否启用范围查询优化。</summary>
    public bool EnableRangeOptimization { get; set; }

    /// <summary>是否启用扩展通配符（后缀/中缀匹配）；前缀始终可用。</summary>
    public bool EnableExtendedWildcard { get; set; }

    /// <summary>构造倒排索引参数。</summary>
    /// <param name="enableRangeOptimization">是否启用范围查询优化。</param>
    /// <param name="enableExtendedWildcard">是否启用扩展通配符。</param>
    public InvertIndexParam(bool enableRangeOptimization = false, bool enableExtendedWildcard = false)
    {
        EnableRangeOptimization = enableRangeOptimization;
        EnableExtendedWildcard = enableExtendedWildcard;
    }
}

/// <summary>全文（BM25）索引，仅用于 STRING 字段。</summary>
public sealed class FtsIndexParam : IndexParam
{
    /// <inheritdoc/>
    public override IndexType Type => IndexType.Fts;

    /// <summary>分词器：standard / ngram / jieba / whitespace。</summary>
    public string TokenizerName { get; set; } = "standard";

    /// <summary>过滤器列表：lowercase / ascii_folding / stemmer。</summary>
    public List<string> Filters { get; set; } = ["lowercase"];

    /// <summary>分词器附加参数（JSON 对象字符串，如 {"ngram_min":2}）。</summary>
    public string ExtraParams { get; set; } = "";

    /// <summary>构造全文索引参数。</summary>
    /// <param name="tokenizerName">分词器名称。</param>
    /// <param name="filters">过滤器列表。</param>
    /// <param name="extraParams">分词器附加参数（JSON）。</param>
    public FtsIndexParam(string tokenizerName = "standard", List<string>? filters = null, string extraParams = "")
    {
        TokenizerName = tokenizerName;
        Filters = filters ?? ["lowercase"];
        ExtraParams = extraParams;
    }
}
