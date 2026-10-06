using Xunit;
using Zvec.NET;
using Zvec.NET.Embedding;
using Zvec.NET.Interop;

namespace Zvec.NET.Tests;

/// <summary>审查补充项的回归测试：库路径解析、Collection.Path、EmbedAsync 安全校验。</summary>
public sealed class ReviewGapTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public ReviewGapTests(ZvecFixture fixture) => _fixture = fixture;

    [Fact]
    public void OpenReturnsOriginalPath()
    {
        string dir = _fixture.NewDir();
        using (Collection created = Zvec.CreateAndOpen(dir, new CollectionSchema("pathchk")
            .AddField(new FieldSchema("t", DataType.String))))
        {
            Assert.Equal(dir, created.Path);
        }

        using Collection opened = Zvec.Open(dir);
        Assert.Equal(dir, opened.Path);
    }

    [Fact]
    public void TrySetLibraryPathAcceptsValidDirectoryAndRejectsMissing()
    {
        // 无效目录被拒绝。
        Assert.False(ZvecNative.TrySetLibraryPath(Path.Combine(_fixture.NewDir(), "no-such-dir")));

        // 指向测试输出目录（含 runtimes/win-x64/native 或平铺 DLL）的注册应在后续原生调用中保持可用。
        string baseDir = AppContext.BaseDirectory;
        Assert.True(ZvecNative.TrySetLibraryPath(baseDir));

        // 追加路径后原生调用仍正常。
        Assert.False(string.IsNullOrEmpty(Zvec.NativeVersion));
    }

    [Fact]
    public async Task EmbedAsyncRejectsLocalBaseUrl()
    {
        // URL 安全校验先于任何网络请求，无需配置凭据。
        using var embedding = new OpenAIEmbedding(baseUrl: "http://localhost:1234/v1");
        await Assert.ThrowsAsync<NotSupportedException>(() => embedding.EmbedAsync("hi"));
    }

    [Fact]
    public void TokenizerHandlesNonAsciiLetters()
    {
        // 非 ASCII 拉丁字母保持连续词元；假名类表意字符不丢失。
        IReadOnlyList<string> tokens = BM25Embedding.Tokenize("café テスト");
        Assert.Contains("café", tokens);
        Assert.Contains("テ", tokens);
        Assert.Contains("ス", tokens);
        Assert.Contains("ト", tokens);
    }

    [Fact]
    public void EmptyDocIdRejected()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("idchk")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentException>(() => collection.Insert(new Doc("",
            fields: new() { ["t"] = "x" })));
    }
}
