using System.Runtime.InteropServices;
using Zvec.NET.Interop;

namespace Zvec.NET;

/// <summary>
/// Zvec 入口（对齐 Python 模块级函数 zvec.init / zvec.open / zvec.create_and_open）。
/// 用法：<c>using Zvec.NET;</c> 后 <c>Zvec.Init(); Zvec.Open(...)</c>。
/// </summary>
public static class Zvec
{
    /// <summary>库版本（如 "0.7.0"），来自原生库。</summary>
    public static string NativeVersion => NativeUtil.PtrToUtf8Required(NativeMethods.zvec_get_version());

    public static int NativeVersionMajor => NativeMethods.zvec_get_version_major();

    public static int NativeVersionMinor => NativeMethods.zvec_get_version_minor();

    public static int NativeVersionPatch => NativeMethods.zvec_get_version_patch();

    public static bool IsInitialized => NativeMethods.zvec_is_initialized();

    /// <summary>
    /// 初始化 Zvec 全局运行时（进程内仅一次；语义对齐 Python zvec.init）。
    /// 必须在所有其他操作之前调用；未设置的项由引擎按环境自适应。
    /// </summary>
    public static void Init(ZvecOptions? options = null)
    {
        options ??= new ZvecOptions();

        IntPtr config = NativeMethods.zvec_config_data_create();
        NativeUtil.ThrowIfNull(config, "config data");
        try
        {
            IntPtr logConfig = BuildLogConfig(options);
            if (logConfig != IntPtr.Zero)
            {
                try
                {
                    NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_log_config(config, logConfig));
                }
                finally
                {
                    // 所有权已转移给 config。
                }
            }

            if (options.QueryThreads is { } queryThreads)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_query_thread_count(config, (uint)queryThreads));
            }

            if (options.OptimizeThreads is { } optimizeThreads)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_optimize_thread_count(config, (uint)optimizeThreads));
            }

            if (options.InvertToForwardScanRatio is { } ratio)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_invert_to_forward_scan_ratio(config, ratio));
            }

            if (options.BruteForceByKeysRatio is { } bfRatio)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_brute_force_by_keys_ratio(config, bfRatio));
            }

            if (options.FtsBruteForceByKeysRatio is { } ftsRatio)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_fts_brute_force_by_keys_ratio(config, ftsRatio));
            }

            if (options.MemoryLimitMb is { } memoryLimitMb)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_memory_limit(
                    config, (ulong)memoryLimitMb * 1024UL * 1024UL));
            }

            if (options.JiebaDictDir is { } jiebaDictDir)
            {
                NativeUtil.ThrowIfError(NativeMethods.zvec_config_data_set_jieba_dict_dir(config, jiebaDictDir));
            }

            NativeUtil.ThrowIfError(NativeMethods.zvec_initialize(config));
        }
        finally
        {
            NativeMethods.zvec_config_data_destroy(config);
        }
    }

    private static IntPtr BuildLogConfig(ZvecOptions options)
    {
        int level = (int)(options.LogLevel ?? LogLevel.Warn);
        if (options.LogType == LogType.File)
        {
            return NativeMethods.zvec_config_log_create_file(
                level,
                options.LogDir ?? "./logs",
                options.LogBasename ?? "zvec.log",
                (uint)(options.LogFileSize ?? 2048),
                (uint)(options.LogOverdueDays ?? 7));
        }

        IntPtr console = NativeMethods.zvec_config_log_create_console(level);
        return console;
    }

    /// <summary>关闭 Zvec 运行时并释放全局资源。</summary>
    public static void Shutdown() => NativeUtil.ThrowIfError(NativeMethods.zvec_shutdown());

    /// <summary>进程级默认 jieba 词典目录（等价 Python 包导入时自动注册 wheel 内词典）。</summary>
    public static void SetDefaultJiebaDictDir(string? dir) => NativeMethods.zvec_set_default_jieba_dict_dir(dir);

    /// <summary>创建并打开集合（对齐 Python zvec.create_and_open）。</summary>
    public static Collection CreateAndOpen(string path, CollectionSchema schema, CollectionOption? option = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        IntPtr nativeSchema = ParamBuilder.BuildCollectionSchema(schema);
        try
        {
            IntPtr nativeOptions = ParamBuilder.BuildCollectionOptions(option);
            try
            {
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_create_and_open(path, nativeSchema, nativeOptions, out IntPtr collection));
            return Collection.FromCreate(collection, path, schema);
            }
            finally
            {
                NativeMethods.zvec_collection_options_destroy(nativeOptions);
            }
        }
        finally
        {
            NativeMethods.zvec_collection_schema_destroy(nativeSchema);
        }
    }

    /// <summary>打开已有集合（对齐 Python zvec.open）。Schema 从磁盘读回。</summary>
    public static Collection Open(string path, CollectionOption? option = null)
    {
        IntPtr nativeOptions = ParamBuilder.BuildCollectionOptions(option);
        try
        {
            NativeUtil.ThrowIfError(NativeMethods.zvec_collection_open(path, nativeOptions, out IntPtr collection));
            return Collection.FromOpen(collection, path);
        }
        finally
        {
            NativeMethods.zvec_collection_options_destroy(nativeOptions);
        }
    }
}
