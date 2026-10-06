namespace Zvec.NET;

/// <summary>查询参数基类。</summary>
public abstract class QueryParam
{
    /// <summary>搜索半径（0 表示不启用）。</summary>
    public float Radius { get; set; }

    /// <summary>是否线性（暴力）检索。</summary>
    public bool IsLinear { get; set; }

    /// <summary>是否使用精排 refiner。</summary>
    public bool IsUsingRefiner { get; set; }
}

/// <summary>HNSW 查询参数。</summary>
public sealed class HnswQueryParam : QueryParam
{
    /// <summary>检索时候选队列大小（默认 300）。</summary>
    public int Ef { get; set; } = 300;

    public HnswQueryParam(int ef = 300, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false)
    {
        Ef = ef;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
    }
}

/// <summary>HNSW RaBitQ 查询参数。</summary>
public sealed class HnswRabitqQueryParam : QueryParam
{
    public int Ef { get; set; } = 300;

    public HnswRabitqQueryParam(int ef = 300, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false)
    {
        Ef = ef;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
    }
}

/// <summary>IVF 查询参数。</summary>
public sealed class IvfQueryParam : QueryParam
{
    /// <summary>探测的聚类数（默认 10）。</summary>
    public int NProbe { get; set; } = 10;

    public IvfQueryParam(int nProbe = 10, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false)
    {
        NProbe = nProbe;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
    }
}

/// <summary>IVF RaBitQ 查询参数。</summary>
public sealed class IvfRabitqQueryParam : QueryParam
{
    public int NProbe { get; set; } = 10;

    /// <summary>refiner 候选扩展倍数（默认 10）。</summary>
    public float ScaleFactor { get; set; } = 10f;

    public IvfRabitqQueryParam(int nProbe = 10, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false,
        float scaleFactor = 10f)
    {
        NProbe = nProbe;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
        ScaleFactor = scaleFactor;
    }
}

/// <summary>Flat 查询参数。</summary>
public sealed class FlatQueryParam : QueryParam
{
    public float ScaleFactor { get; set; } = 10f;

    public FlatQueryParam(float radius = 0f, bool isLinear = false, bool isUsingRefiner = false, float scaleFactor = 10f)
    {
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
        ScaleFactor = scaleFactor;
    }
}

/// <summary>Vamana 查询参数。</summary>
public sealed class VamanaQueryParam : QueryParam
{
    /// <summary>检索时候选队列大小（默认 200）。</summary>
    public int EfSearch { get; set; } = 200;

    public VamanaQueryParam(int efSearch = 200, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false)
    {
        EfSearch = efSearch;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
    }
}

/// <summary>DiskANN 查询参数。</summary>
public sealed class DiskAnnQueryParam : QueryParam
{
    /// <summary>beam search 候选队列大小（默认 300）。</summary>
    public int ListSize { get; set; } = 300;

    public DiskAnnQueryParam(int listSize = 300, float radius = 0f, bool isLinear = false, bool isUsingRefiner = false)
    {
        ListSize = listSize;
        Radius = radius;
        IsLinear = isLinear;
        IsUsingRefiner = isUsingRefiner;
    }
}

/// <summary>FTS 全文查询参数。</summary>
public sealed class FtsQueryParam : QueryParam
{
    /// <summary>裸词间默认布尔算子："OR"（默认）/ "AND"。</summary>
    public string DefaultOperator { get; set; } = "";

    public FtsQueryParam(string defaultOperator = "")
    {
        DefaultOperator = defaultOperator;
    }
}
