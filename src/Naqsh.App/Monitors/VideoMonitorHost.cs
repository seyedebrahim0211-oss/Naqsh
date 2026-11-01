using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;

namespace Naqsh.App.Monitors;

/// <summary>
/// WPF element hosting a child HWND with a flip-model swap chain (not D3DImage), so video is
/// presented by DWM independently of WPF rendering.
/// </summary>
public sealed class VideoMonitorHost : HwndHost
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(MonitorSource),
        typeof(VideoMonitorHost),
        new PropertyMetadata(null, (d, e) => ((VideoMonitorHost)d)._presenter?.SetSource((MonitorSource?)e.NewValue)));

    private MonitorPresenter? _presenter;
    private IntPtr _hwnd;

    public MonitorSource? Source
    {
        get => (MonitorSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = NativeMethods.CreateWindowEx(
            0,
            "static",
            string.Empty,
            NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipSiblings,
            0,
            0,
            (int)Math.Max(1, ActualWidth),
            (int)Math.Max(1, ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }

        _presenter = new MonitorPresenter(_hwnd, Log.Logger);
        _presenter.SetSource(Source);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _presenter?.Dispose();
        _presenter = null;
        NativeMethods.DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }
}
