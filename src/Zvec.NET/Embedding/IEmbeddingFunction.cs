namespace Zvec.NET;

/// <summary>稠密向量嵌入接口（对齐 Python DenseEmbeddingFunction）。</summary>
public interface IDenseEmbeddingFunction
{
    /// <summary>把文本编码为稠密向量（FP32）。</summary>
    float[] Embed(string input);
}

/// <summary>稀疏向量嵌入接口（对齐 Python SparseEmbeddingFunction）。</summary>
public interface ISparseEmbeddingFunction
{
    /// <summary>把文本编码为稀疏向量。</summary>
    SparseVector Embed(string input);
}
