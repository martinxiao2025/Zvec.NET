namespace Zvec.NET.Interop;

/// <summary>zvec 原生错误码（与 c_api.h 中 zvec_error_code_t 一一对应）。</summary>
public enum ZvecErrorCode : int
{
    Ok = 0,
    NotFound = 1,
    AlreadyExists = 2,
    InvalidArgument = 3,
    PermissionDenied = 4,
    FailedPrecondition = 5,
    ResourceExhausted = 6,
    Unavailable = 7,
    InternalError = 8,
    NotSupported = 9,
    Unknown = 10,
}

/// <summary>调用 zvec 原生库失败时抛出的异常，附带原生侧的错误详情。</summary>
public sealed class ZvecException : Exception
{
    public ZvecErrorCode ErrorCode { get; }

    public string? NativeFile { get; }

    public int NativeLine { get; }

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
