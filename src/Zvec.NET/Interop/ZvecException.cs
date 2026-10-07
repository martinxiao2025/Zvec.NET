namespace Zvec.NET.Interop;

/// <summary>zvec 原生错误码（与 c_api.h 中 zvec_error_code_t 一一对应）。</summary>
public enum ZvecErrorCode
{
    /// <summary>成功。</summary>
    Ok = 0,
    /// <summary>目标不存在。</summary>
    NotFound = 1,
    /// <summary>目标已存在。</summary>
    AlreadyExists = 2,
    /// <summary>非法参数。</summary>
    InvalidArgument = 3,
    /// <summary>权限不足。</summary>
    PermissionDenied = 4,
    /// <summary>前置条件不满足。</summary>
    FailedPrecondition = 5,
    /// <summary>资源不足（内存/句柄等）。</summary>
    ResourceExhausted = 6,
    /// <summary>服务不可用。</summary>
    Unavailable = 7,
    /// <summary>引擎内部错误。</summary>
    InternalError = 8,
    /// <summary>能力不支持。</summary>
    NotSupported = 9,
    /// <summary>未归类错误。</summary>
    Unknown = 10,
}

/// <summary>调用 zvec 原生库失败时抛出的异常，附带原生侧的错误详情。</summary>
public sealed class ZvecException : Exception
{
    /// <summary>原生错误码。</summary>
    public ZvecErrorCode ErrorCode { get; }

    /// <summary>原生错误发生的源文件（若原生侧提供）。</summary>
    public string? NativeFile { get; }

    /// <summary>原生错误行号（0 表示未知）。</summary>
    public int NativeLine { get; }

    /// <summary>原生错误所在函数（若原生侧提供）。</summary>
    public string? NativeFunction { get; }

    internal ZvecException(ZvecErrorCode code, string message, string? file = null, int line = 0, string? function = null)
        : base($"[{code}] {message}")
    {
        ErrorCode = code;
        NativeFile = file;
        NativeLine = line;
        NativeFunction = function;
    }
}
