using System.Runtime.InteropServices;
using System.Text;

namespace Zvec.NET.Interop;

internal static unsafe class NativeUtil
{
    /// <summary>非 ZVEC_OK 时读取原生错误详情并抛出 ZvecException。</summary>
    internal static void ThrowIfError(int code, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (code == 0)
        {
            return;
        }

        string message = caller;
        string? file = null;
        int line = 0;
        string? function = null;
        if (NativeMethods.zvec_get_last_error_details(out ZvecErrorDetails details) == 0)
        {
            message = PtrToUtf8(details.Message) ?? caller;
            file = PtrToUtf8(details.File);
            line = details.Line;
            function = PtrToUtf8(details.Function);
        }
        else if (NativeMethods.zvec_get_last_error(out IntPtr msg) == 0 && msg != IntPtr.Zero)
        {
            message = PtrToUtf8(msg) ?? caller;
            NativeMethods.zvec_free(msg);
        }

        NativeMethods.zvec_clear_error();
        throw new ZvecException((ZvecErrorCode)code, $"{message}（调用点: {caller}）", file, line, function);
    }

    internal static void ThrowIfNull(IntPtr handle, string what)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ZvecException(ZvecErrorCode.InternalError, $"原生对象创建失败: {what}");
        }
    }

    internal static string? PtrToUtf8(IntPtr ptr) => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

    internal static string PtrToUtf8Required(IntPtr ptr) =>
        PtrToUtf8(ptr) ?? throw new ZvecException(ZvecErrorCode.InternalError, "原生返回了空字符串指针");
}

/// <summary>
/// 一段由调用方分配并需要释放的原生内存。用于组装 byte** / IntPtr* 数组等临时结构。
/// </summary>
internal sealed unsafe class NativeArena : IDisposable
{
    private readonly List<IntPtr> _allocations = [];

    public void* Alloc(nuint bytes)
    {
        IntPtr ptr = Marshal.AllocHGlobal(checked((int)bytes));
        _allocations.Add(ptr);
        return (void*)ptr;
    }

    /// <summary>把一组字符串编码为 UTF-8 的 byte* 数组（NULL 元素编码为 null 指针）。返回数组首地址，count 为元素数。</summary>
    public byte** AllocUtf8Array(IReadOnlyList<string?> values, out nuint count)
    {
        count = (nuint)values.Count;
        if (count == 0)
        {
            return null;
        }

        byte** array = (byte**)Alloc(count * (nuint)sizeof(IntPtr));
        for (int i = 0; i < values.Count; i++)
        {
            string? value = values[i];
            array[i] = value is null ? null : (byte*)AllocUtf8(value);
        }

        return array;
    }

    public byte* AllocUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetMaxByteCount(value.Length) + 1;
        byte* buffer = (byte*)Alloc((nuint)byteCount);
        int written = Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, byteCount));
        buffer[written] = 0;
        return buffer;
    }

    public void Dispose()
    {
        foreach (IntPtr ptr in _allocations)
        {
            Marshal.FreeHGlobal(ptr);
        }

        _allocations.Clear();
    }
}

/// <summary>托管非拥有所有权的原生对象的 SafeHandle 基类；子类指定释放函数。</summary>
internal abstract class NativeHandle : SafeHandle
{
    protected NativeHandle(IntPtr handle)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;
}

/// <summary>zvec_collection_t（ReleaseHandle 调用 zvec_collection_close）。</summary>
internal sealed class CollectionHandle : NativeHandle
{
    public CollectionHandle(IntPtr handle) : base(handle) { }

    protected override bool ReleaseHandle()
    {
        // close 失败（如仍有迭代器未结束）时返回 false，由 SafeHandle 记录 ReleaseHandle 失败事件。
        return NativeMethods.zvec_collection_close(handle) == 0;
    }
}

/// <summary>zvec_doc_t。</summary>
internal sealed class DocHandle : NativeHandle
{
    public DocHandle(IntPtr handle) : base(handle) { }

    protected override bool ReleaseHandle()
    {
        NativeMethods.zvec_doc_destroy(handle);
        return true;
    }
}

/// <summary>zvec_doc_iterator_t。</summary>
internal sealed class DocIteratorHandle : NativeHandle
{
    public DocIteratorHandle(IntPtr handle) : base(handle) { }

    protected override bool ReleaseHandle()
    {
        NativeMethods.zvec_doc_iterator_close(handle);
        return true;
    }
}

/// <summary>带自定义释放逻辑的通用原生句柄。</summary>
internal sealed class DelegateHandle : NativeHandle
{
    private readonly Action<IntPtr> _release;

    public DelegateHandle(IntPtr handle, Action<IntPtr> release) : base(handle)
    {
        _release = release;
    }

    protected override bool ReleaseHandle()
    {
        _release(handle);
        return true;
    }
}
