namespace Zvec.NET.EntityFrameworkCore;

/// <summary>
/// 标注实体对应的 Zvec 集合（可省略，默认使用实体类型名）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class VectorCollectionAttribute : Attribute
{
    /// <summary>集合名（同时作为 create_and_open 目录的默认子目录名时由调用方决定，此名用于 schema.name）。</summary>
    public string Name { get; }

    /// <summary>标注集合名。</summary>
    /// <param name="name">集合名。</param>
    public VectorCollectionAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }
}

/// <summary>
/// 标注实体的主键属性（对应 Doc.Id / zvec 主键）。
/// 未标注时按约定取名为 "Id" 或 "&lt;实体名&gt;Id" 的属性。
/// 支持 string / int / long / Guid；非 string 键以 ToString() 格式化为字符串存储
/// （写入与查询两侧使用同一格式化表达式，保证对齐）。
/// 注意：仅属性可标注（映射只读取属性，公有字段会被忽略）。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VectorKeyAttribute : Attribute
{
}

/// <summary>
/// 标注向量属性（float[] 稠密 FP32 或 <see cref="SparseVector"/> 稀疏）。
/// 注意：仅属性可标注（映射只读取属性，公有字段会被忽略）。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VectorFieldAttribute : Attribute
{
    /// <summary>稠密向量维度（稀疏向量为 0）。</summary>
    public int Dimension { get; set; }

    /// <summary>标注向量属性。</summary>
    /// <param name="dimension">稠密维度（稀疏为 0）。</param>
    public VectorFieldAttribute(int dimension = 0)
    {
        Dimension = dimension;
    }
}

/// <summary>标注不参与向量集合同步的属性。注意：仅属性可标注。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VectorIgnoredAttribute : Attribute
{
}
