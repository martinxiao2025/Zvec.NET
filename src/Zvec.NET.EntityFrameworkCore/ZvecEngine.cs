namespace Zvec.NET.EntityFrameworkCore;

/// <summary>引擎初始化的进程内门闩：并发首触达时串行化 IsInitialized 检查，避免双初始化异常。</summary>
internal static class ZvecEngine
{
    private static readonly Lock InitLock = new();

    /// <summary>未初始化时按配置初始化（进程内一次）；已初始化则跳过（后续配置不再生效）。</summary>
    public static void EnsureInitialized(Action<ZvecOptions>? configure = null)
    {
        if (global::Zvec.NET.Zvec.IsInitialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (!global::Zvec.NET.Zvec.IsInitialized)
            {
                ZvecOptions options = new();
                configure?.Invoke(options);
                global::Zvec.NET.Zvec.Init(options);
            }
        }
    }
}
