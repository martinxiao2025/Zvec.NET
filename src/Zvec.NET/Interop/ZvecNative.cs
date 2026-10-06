using System.Reflection;
using System.Runtime.InteropServices;

namespace Zvec.NET.Interop;

/// <summary>
/// 原生库解析（基于 NativeLibrary.SetDllImportResolver 的官方模式）。
/// 解析顺序：ZvecNative.TrySetLibraryPath 指定的目录 → 程序集目录平铺 →
/// 程序集目录下 runtimes/{RID}/native（NuGet 标准布局）→ 默认探测（PATH 等）。
/// Windows 下 zvec_c_api.dll 依赖 zvec/zvec_core/zvec_ailego，解析主库前按依赖顺序预加载。
/// </summary>
public static class ZvecNative
{
    private const string EntryLibrary = "zvec_c_api";
    private static readonly string[] DependenciesBeforeEntry = ["zvec_ailego", "zvec_core", "zvec"];

    private static readonly Lock Gate = new();
    private static List<string>? _customDirectories;
    private static bool _resolverInstalled;

    /// <summary>模块加载时安装 DllImport 解析器（必须早于首次 P/Invoke；不主动加载库）。</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void ModuleInitialize()
    {
        lock (Gate)
        {
            InstallResolverIfNeeded();
        }
    }

    /// <summary>
    /// 追加一个原生库搜索目录（可在任意时刻调用；立即生效于后续原生调用）。
    /// 也可以直接传入 zvec_c_api 库文件完整路径。返回路径是否有效。
    /// </summary>
    public static bool TrySetLibraryPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string directory = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path))!;

        if (!Directory.Exists(directory))
        {
            return false;
        }

        lock (Gate)
        {
            InstallResolverIfNeeded();
            _customDirectories ??= [];
            if (!_customDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                _customDirectories.Add(directory);
            }
        }

        return true;
    }

    private static void InstallResolverIfNeeded()
    {
        if (_resolverInstalled)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(ZvecNative).Assembly, ResolveImport);
        _resolverInstalled = true;
    }

    private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Contains(EntryLibrary, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero; // 非本库托管的原生依赖，走默认解析。
        }

        List<string> directories = CandidateDirectories();
        foreach (string directory in directories)
        {
            // 先按依赖顺序预加载（Windows 静态导入需能按模块名命中）。
            foreach (string dependency in DependenciesBeforeEntry)
            {
                string? dependencyPath = FindLibraryFile(directory, dependency);
                if (dependencyPath is not null)
                {
                    NativeLibrary.TryLoad(dependencyPath, out _);
                }
            }

            string? entryPath = FindLibraryFile(directory, EntryLibrary);
            if (entryPath is not null && NativeLibrary.TryLoad(entryPath, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero; // 交给默认探测兜底。
    }

    private static List<string> CandidateDirectories()
    {
        var directories = new List<string>();
        lock (Gate)
        {
            if (_customDirectories is not null)
            {
                directories.AddRange(_customDirectories);
            }
        }

        string? assemblyDir = GetAssemblyDirectory();
        if (assemblyDir is not null)
        {
            directories.Add(assemblyDir);
            directories.Add(Path.Combine(assemblyDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"));
        }

        return directories;
    }

    private static string? GetAssemblyDirectory()
    {
        try
        {
            string? location = typeof(ZvecNative).Assembly.Location;
            return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        }
        catch
        {
            return null;
        }
    }

    private static string LibraryFileName(string name) =>
        OperatingSystem.IsWindows() ? name + ".dll"
        : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib"
        : "lib" + name + ".so";

    private static string? FindLibraryFile(string directory, string name)
    {
        string candidate = Path.Combine(directory, LibraryFileName(name));
        return File.Exists(candidate) ? candidate : null;
    }
}
