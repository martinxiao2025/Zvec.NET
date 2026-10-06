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
    public abstract IndexType Type { get; }
}

/// <summary>向量索引参数基类。</summary>
public abstract class VectorIndexParam : IndexParam
{
    public MetricType MetricType { get; set; } = MetricType.Undefined;

    public QuantizeType QuantizeType { get; set; } = QuantizeType.Undefined;

    public QuantizerParam QuantizerParam { get; set; } = new();
}

/// <summary>Flat 精确检索索引（默认 IP 度量）。</summary>
public sealed class FlatIndexParam : VectorIndexParam
{
    public override IndexType Type => IndexType.Flat;

    public FlatIndexParam(MetricType metricType = MetricType.Ip, QuantizeType quantizeType = QuantizeType.Undefined)
    {
        MetricType = metricType;
        QuantizeType = quantizeType;
    }
}

/// <summary>HNSW 图索引。</summary>
public sealed class HnswIndexParam : VectorIndexParam
{
    public override IndexType Type => IndexType.Hnsw;

    /// <summary>每个节点的双向连接数（默认 50）。</summary>
    public int M { get; set; } = 50;

    /// <summary>建图时候选列表大小（默认 500）。</summary>
    public int EfConstruction { get; set; } = 500;

    /// <summary>是否使用连续内存布局。</summary>
    public bool UseContiguousMemory { get; set; }

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
    public override IndexType Type => IndexType.HnswRabitq;

    public HnswRabitqIndexParam(MetricType metricType = MetricType.Ip)
    {
        MetricType = metricType;
        QuantizeType = QuantizeType.Rabitq;
    }
}

/// <summary>IVF 倒排文件索引。</summary>
public sealed class IvfIndexParam : VectorIndexParam
{
    public override IndexType Type => IndexType.Ivf;

    /// <summary>聚类中心数（默认 10）。</summary>
    public int NList { get; set; } = 10;

    /// <summary>k-means 迭代次数（默认 10）。</summary>
    public int NIters { get; set; } = 10;

    /// <summary>是否启用 SOAR 优化。</summary>
    public bool UseSoar { get; set; }

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
    public override IndexType Type => IndexType.IvfRabitq;

    /// <summary>聚类中心数（默认 1024）。</summary>
    public int NList { get; set; } = 1024;

    /// <summary>RaBitQ 量化总位数（默认 7）。</summary>
    public int TotalBits { get; set; } = 7;

    /// <summary>训练采样数，0 表示使用全部向量。</summary>
    public int SampleCount { get; set; }

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
    public override IndexType Type => IndexType.DiskAnn;

    /// <summary>图最大度，范围 [1,100]（默认 100）。</summary>
    public int MaxDegree { get; set; } = 100;

    /// <summary>建图候选列表大小，范围 [10,100]（默认 50）。</summary>
    public int ListSize { get; set; } = 50;

    /// <summary>PQ 分块数，范围 [1,1024]，0 = 自动（默认）。</summary>
    public int PqChunkNum { get; set; }

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
    public override IndexType Type => IndexType.Vamana;

    public int MaxDegree { get; set; } = 64;

    public int SearchListSize { get; set; } = 100;

    public float Alpha { get; set; } = 1.2f;

    public bool SaturateGraph { get; set; }

    public bool UseContiguousMemory { get; set; }

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
    public override IndexType Type => IndexType.Invert;

    /// <summary>是否启用范围查询优化。</summary>
    public bool EnableRangeOptimization { get; set; }

    /// <summary>是否启用扩展通配符（后缀/中缀匹配）；前缀始终可用。</summary>
    public bool EnableExtendedWildcard { get; set; }

    public InvertIndexParam(bool enableRangeOptimization = false, bool enableExtendedWildcard = false)
    {
        EnableRangeOptimization = enableRangeOptimization;
        EnableExtendedWildcard = enableExtendedWildcard;
    }
}

/// <summary>全文（BM25）索引，仅用于 STRING 字段。</summary>
public sealed class FtsIndexParam : IndexParam
{
    public override IndexType Type => IndexType.Fts;

    /// <summary>分词器：standard / ngram / jieba / whitespace。</summary>
    public string TokenizerName { get; set; } = "standard";

    /// <summary>过滤器列表：lowercase / ascii_folding / stemmer。</summary>
    public List<string> Filters { get; set; } = ["lowercase"];

    /// <summary>分词器附加参数（JSON 对象字符串，如 {"ngram_min":2}）。</summary>
    public string ExtraParams { get; set; } = "";

    public FtsIndexParam(string tokenizerName = "standard", List<string>? filters = null, string extraParams = "")
    {
        TokenizerName = tokenizerName;
        Filters = filters ?? ["lowercase"];
        ExtraParams = extraParams;
    }
}
