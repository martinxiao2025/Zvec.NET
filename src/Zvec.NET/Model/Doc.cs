using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>
/// 稀疏向量：非零维度的索引与值。索引升序排列。
/// 对应 Python 的 dict[int, float]。
/// </summary>
public sealed class SparseVector
{
    /// <summary>非零维度索引（升序）。</summary>
    public uint[] Indices { get; }

    /// <summary>与 <see cref="Indices"/> 一一对应的非零值。</summary>
    public float[] Values { get; }

    /// <summary>非零元素个数。</summary>
    public int Count => Indices.Length;

    /// <summary>以索引数组与值数组构造稀疏向量（长度必须一致，建议索引升序）。</summary>
    /// <param name="indices">维度索引。</param>
    /// <param name="values">非零值。</param>
    public SparseVector(uint[] indices, float[] values)
    {
        if (indices.Length != values.Length)
        {
            throw new ArgumentException("稀疏向量的 indices 与 values 长度必须一致。");
        }

        Indices = indices;
        Values = values;
    }

    /// <summary>从字典（维度 → 值）构造，索引按升序排列。</summary>
    /// <param name="map">维度到值的映射。</param>
    public static SparseVector FromDictionary(IReadOnlyDictionary<long, float> map)
    {
        var keys = map.Keys.OrderBy(k => k).ToArray();
        var indices = new uint[keys.Length];
        var values = new float[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            long key = keys[i];
            // 稀疏向量维度须能表示为 uint；负值 or 大于 uint.MaxValue 给出明确的参数异常。
            if ((ulong)key > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(map), key,
                    $"稀疏向量维度 {key} 超出 uint 范围（0..{uint.MaxValue}）。");
            }

            indices[i] = (uint)key;
            values[i] = map[key];
        }

        return new SparseVector(indices, values);
    }

    /// <summary>转换为字典（维度 → 值）。</summary>
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
    /// <summary>文档主键（字符串）。</summary>
    public string Id { get; set; }

    /// <summary>检索得分（仅查询结果携带）。</summary>
    public float? Score { get; set; }

    /// <summary>向量字段集：字段名 → 向量值。</summary>
    public Dictionary<string, object> Vectors { get; set; } = [];

    /// <summary>标量字段集：字段名 → 值（可为 null 表示 NULL）。</summary>
    public Dictionary<string, object?> Fields { get; set; } = [];

    /// <summary>构造文档。</summary>
    /// <param name="id">主键。</param>
    /// <param name="score">检索得分（通常仅查询结果使用）。</param>
    /// <param name="vectors">向量字段初始内容。</param>
    /// <param name="fields">标量字段初始内容。</param>
    public Doc(string id, float? score = null, Dictionary<string, object>? vectors = null, Dictionary<string, object?>? fields = null)
    {
        Id = id;
        Score = score;
        Vectors = vectors ?? [];
        Fields = fields ?? [];
    }

    /// <summary>是否包含指定标量字段。</summary>
    public bool HasField(string name) => Fields.ContainsKey(name);

    /// <summary>是否包含指定向量字段。</summary>
    public bool HasVector(string name) => Vectors.ContainsKey(name);

    /// <summary>读取标量字段值；不存在返回 null。</summary>
    public object? Field(string name) => Fields.TryGetValue(name, out object? value) ? value : null;

    /// <summary>读取向量字段值；不存在返回 null。</summary>
    public object? Vector(string name) => Vectors.TryGetValue(name, out object? value) ? value : null;
}

/// <summary>单文档写入结果（对齐 Python zvec.Status）。</summary>
public readonly struct WriteResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; }

    /// <summary>失败时的错误码。</summary>
    public ZvecErrorCode ErrorCode { get; }

    /// <summary>结果描述（成功为 OK，失败为原生错误消息）。</summary>
    public string Message { get; }

    /// <summary>构造写入结果。</summary>
    /// <param name="success">是否成功。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">描述信息。</param>
    public WriteResult(bool success, ZvecErrorCode errorCode, string message)
    {
        Success = success;
        ErrorCode = errorCode;
        Message = message;
    }

    /// <summary>返回 "OK" 或 "[错误码] 消息"。</summary>
    public override string ToString() => Success ? "OK" : $"[{ErrorCode}] {Message}";
}
