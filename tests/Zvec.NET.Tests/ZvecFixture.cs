using Xunit;

namespace Zvec.NET.Tests;

/// <summary>Zvec 全局初始化（进程内仅一次）与临时目录管理。</summary>
public sealed class ZvecFixture : IDisposable
{
    private static readonly Lock InitLock = new();
    private static bool _initialized;
    private readonly string _root;

    public ZvecFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "zvec-net-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        lock (InitLock)
        {
            if (!_initialized)
            {
                Zvec.Init(new ZvecOptions { LogLevel = LogLevel.Error });
                _initialized = true;
            }
        }
    }

    /// <summary>返回一个尚不存在的集合目录路径（zvec create 要求目标路径不存在）。</summary>
    public string NewDir() => Path.Combine(_root, Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows 句柄延迟释放时忽略。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
