using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>
/// 稀疏向量：非零维度的索引与值。索引升序排列。
/// 对应 Python 的 dict[int, float]。
/// </summary>
public sealed class SparseVector
{
    public uint[] Indices { get; }

    public float[] Values { get; }

    public int Count => Indices.Length;

    public SparseVector(uint[] indices, float[] values)
    {
        if (indices.Length != values.Length)
        {
            throw new ArgumentException("稀疏向量的 indices 与 values 长度必须一致。");
        }

        Indices = indices;
        Values = values;
    }

    public static SparseVector FromDictionary(IReadOnlyDictionary<long, float> map)
    {
        var keys = map.Keys.OrderBy(k => k).ToArray();
        var indices = new uint[keys.Length];
        var values = new float[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            indices[i] = checked((uint)keys[i]);
            values[i] = map[keys[i]];
        }

        return new SparseVector(indices, values);
    }

    public IReadOnlyDictionary<long, float> ToDictionary()
    {
        var dict = new Dictionary<long, float>(Count);
        for (int i = 0; i < Count; i++)
        {
            dict[Indices[i]] = Values[i];
        }

        return dict;
    }
}

/// <summary>
/// 文档：基本存储单元（对齐 Python zvec.Doc）。
/// Vectors 的值支持 float[]（FP32）、double[]（FP64）、Half[]（FP16）、sbyte[]（INT8）与 <see cref="SparseVector"/>。
/// </summary>
public sealed class Doc
{
    public string Id { get; set; }

    /// <summary>检索得分（仅查询结果携带）。</summary>
    public float? Score { get; set; }

    public Dictionary<string, object> Vectors { get; set; } = [];

    public Dictionary<string, object?> Fields { get; set; } = [];

    public Doc(string id, float? score = null, Dictionary<string, object>? vectors = null, Dictionary<string, object?>? fields = null)
    {
        Id = id;
        Score = score;
        Vectors = vectors ?? [];
        Fields = fields ?? [];
    }

    public bool HasField(string name) => Fields.ContainsKey(name);

    public bool HasVector(string name) => Vectors.ContainsKey(name);

    public object? Field(string name) => Fields.TryGetValue(name, out object? value) ? value : null;

    public object? Vector(string name) => Vectors.TryGetValue(name, out object? value) ? value : null;
}

/// <summary>单文档写入结果（对齐 Python zvec.Status）。</summary>
public readonly struct WriteResult
{
    public bool Success { get; }

    public ZvecErrorCode ErrorCode { get; }

    public string Message { get; }

    public WriteResult(bool success, ZvecErrorCode errorCode, string message)
    {
        Success = success;
        ErrorCode = errorCode;
        Message = message;
    }

    public override string ToString() => Success ? "OK" : $"[{ErrorCode}] {Message}";
}
