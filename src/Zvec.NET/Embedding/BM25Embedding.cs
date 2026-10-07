using System.Globalization;
using System.Text;

namespace Zvec.NET.Embedding;

/// <summary>
/// 本地 BM25 稀疏嵌入（对齐 Python BM25EmbeddingFunction 的 corpus 训练路径）。
/// 与 Python 版差异：不依赖 dashtext 预训练编码器——必须提供 corpus 在本地训练；
/// 分词采用 CJK 单字 + 拉丁单词的通用策略（query/document 两种编码模式）。
/// </summary>
public sealed class BM25Embedding : ISparseEmbeddingFunction
{
    private readonly Dictionary<string, uint> _vocabulary = [];
    private readonly double[] _idf;
    private readonly int _corpusSize;
    private readonly double _averageDocLength;
    private readonly double _k1;
    private readonly double _b;
    private readonly bool _queryMode;

    /// <summary>训练语料文档数。</summary>
    public int CorpusSize => _corpusSize;

    /// <param name="corpus">训练语料（必须非空）。</param>
    /// <param name="encodingType">"query"（默认）或 "document"：query 模式仅按 IDF 加权，document 模式计算完整 BM25 权重。</param>
    /// <param name="b">文档长度归一化 [0,1]（默认 0.75）。</param>
    /// <param name="k1">词频饱和度（默认 1.2）。</param>
    public BM25Embedding(IEnumerable<string> corpus, string encodingType = "query",
        double b = 0.75, double k1 = 1.2)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (encodingType is not ("query" or "document"))
        {
            throw new ArgumentException("encodingType 必须是 query 或 document。", nameof(encodingType));
        }

        string[] documents = [.. corpus];
        if (documents.Length == 0)
        {
            throw new ArgumentException("BM25 训练语料不能为空。", nameof(corpus));
        }

        _queryMode = encodingType == "query";
        _b = b;
        _k1 = k1;
        _corpusSize = documents.Length;

        var docTermFreqs = new List<Dictionary<string, int>>(documents.Length);
        var documentFrequency = new Dictionary<string, int>();
        long totalLength = 0;

        foreach (string document in documents)
        {
            if (document is null)
            {
                throw new ArgumentException("训练语料包含 null 文档。", nameof(corpus));
            }

            Dictionary<string, int> termFreqs = CountTokens(Tokenize(document));
            docTermFreqs.Add(termFreqs);
            totalLength += termFreqs.Values.Sum();
            foreach (string term in termFreqs.Keys)
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        _averageDocLength = totalLength / (double)documents.Length;

        _idf = new double[documentFrequency.Count];
        uint nextId = 0;
        foreach (KeyValuePair<string, int> pair in documentFrequency)
        {
            uint id = nextId++;
            _vocabulary[pair.Key] = id;
            // BM25+ 平滑 IDF：ln(1 + (N - df + 0.5)/(df + 0.5))；词项 ID 为 0..N-1 稠密编号，数组直取。
            _idf[id] = Math.Log(1.0 + (_corpusSize - pair.Value + 0.5) / (pair.Value + 0.5));
        }
    }

    /// <summary>编码模式："query"（仅 IDF 加权）或 "document"（完整 BM25 权重）。</summary>
    public string EncodingType => _queryMode ? "query" : "document";

    /// <summary>查询词表项 ID（测试与诊断用途）。</summary>
    internal bool TryGetTermId(string term, out uint termId) => _vocabulary.TryGetValue(term, out termId);

    /// <summary>把文本编码为 BM25 稀疏向量（词表外的词被忽略；非正权重被过滤）。</summary>
    /// <param name="input">输入文本。</param>
    public SparseVector Embed(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);

        Dictionary<string, int> termFreqs = CountTokens(Tokenize(input));
        // 仅 document 模式使用文档长度做归一化；query 模式只按 IDF 加权。
        double docLength = termFreqs.Values.Sum();

        var entries = new List<KeyValuePair<uint, float>>();
        foreach (KeyValuePair<string, int> pair in termFreqs)
        {
            if (!_vocabulary.TryGetValue(pair.Key, out uint termId))
            {
                continue;
            }

            double tf = pair.Value;
            double weight = _queryMode
                ? _idf[termId]
                : _idf[termId] * (tf * (_k1 + 1.0)) / (tf + _k1 * (1.0 - _b + _b * (docLength / Math.Max(_averageDocLength, 1))));
            if (weight > 0)
            {
                entries.Add(new(termId, (float)weight));
            }
        }

        entries.Sort((a, b) => a.Key.CompareTo(b.Key));
        return new SparseVector([.. entries.Select(e => e.Key)], [.. entries.Select(e => e.Value)]);
    }

    /// <summary>CJK 字符按单字切分；其他表意文字（假名等）单字成词；字母/数字按连续词元切分（小写化，索引切片）。
    /// 按 Unicode 码位（Rune）迭代：增补平面字符（如 CJK 扩展 B）作为整体成词，不会被拆成孤立代理项。</summary>
    internal static IReadOnlyList<string> Tokenize(string text)
    {
        List<string> tokens = [];
        string lowered = text.ToLowerInvariant();
        int start = -1;

        void Flush(int end)
        {
            if (end > start && start >= 0)
            {
                tokens.Add(lowered.Substring(start, end - start));
            }

            start = -1;
        }

        int i = 0;
        while (i < lowered.Length)
        {
            Rune.DecodeFromUtf16(lowered.AsSpan(i), out Rune rune, out int consumed);
            if (IsCjk(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherLetter)
            {
                Flush(i);
                tokens.Add(lowered.Substring(i, consumed));
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else
            {
                Flush(i);
            }

            i += consumed;
        }

        Flush(lowered.Length);
        return tokens;
    }

    private static bool IsCjk(Rune rune) => rune.Value is >= 0x4E00 and <= 0x9FFF
        or >= 0x3400 and <= 0x4DBF
        or >= 0xF900 and <= 0xFAFF;

    private static Dictionary<string, int> CountTokens(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<string, int>();
        foreach (string token in tokens)
        {
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        return counts;
    }
}
