using Vortice.DXGI;

namespace Naqsh.Engine.Gpu;

/// <summary>
/// Allocation-free IDXGIKeyedMutex calls that report WAIT_TIMEOUT (the managed wrapper hides it,
/// because WAIT_TIMEOUT is a success HRESULT).
/// </summary>
public static unsafe class KeyedMutex
{
    private const int WaitAbandoned = 0x80;

    // IUnknown (3) + IDXGIObject (4) + IDXGIDeviceSubObject (1) → AcquireSync = 8, ReleaseSync = 9.
    private const int AcquireSyncSlot = 8;
    private const int ReleaseSyncSlot = 9;

    /// <summary>Tries to acquire the mutex. Returns false on timeout (0x102) or failure.</summary>
    public static bool TryAcquire(IDXGIKeyedMutex mutex, ulong key, uint milliseconds)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        var p = mutex.NativePointer;
        var vtbl = *(void***)p;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)vtbl[AcquireSyncSlot])(p, key, milliseconds);
        return hr is 0 or WaitAbandoned;
    }

    public static void Release(IDXGIKeyedMutex mutex, ulong key)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        var p = mutex.NativePointer;
        var vtbl = *(void***)p;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)vtbl[ReleaseSyncSlot])(p, key);
    }
}
