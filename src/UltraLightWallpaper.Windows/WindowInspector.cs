using System.Runtime.InteropServices;
using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

public sealed class WindowInspector : IDisposable
{
    private IVirtualDesktopManager? virtualDesktops;
    private bool disposed;
    private readonly nint region = Native.CreateRectRgn(0, 0, 0, 0);
    private static readonly HashSet<string> SystemClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "SHELLDLL_DefView", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "MultitaskingViewFrame", "TaskSwitcherWnd", "tooltips_class32", "XamlExplorerHostIslandWindow", "ForegroundStaging"
    };
    public WindowInspector()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (type is not null) virtualDesktops = (IVirtualDesktopManager?)Activator.CreateInstance(type);
        }
        catch (COMException e) { Log.Write("Virtual desktop manager unavailable; DWM cloaking remains active.", e); }
    }

    public static IReadOnlyList<MonitorSnapshot> GetMonitors()
    {
        var result = new List<MonitorSnapshot>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref Native.Rect bounds, nint data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            var device = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
            Native.EnumDisplayDevices(info.Device, 0, ref device, 1); // EDD_GET_DEVICE_INTERFACE_NAME: stable monitor interface path.
            string id = string.IsNullOrEmpty(device.DeviceId) ? info.Device : device.DeviceId;
            if (result.Any(m => m.Id == id)) id += ":" + info.Device;
            uint dpi = Native.GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? x : 96;
            result.Add(new(id, info.Monitor.Pixels, info.Work.Pixels, dpi, string.IsNullOrWhiteSpace(device.DeviceString) ? info.Device : device.DeviceString, (info.Flags & 1) != 0));
            return true;
        }, 0);
        return result.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToArray();
    }

    public IReadOnlyList<WindowSnapshot> Capture(IReadOnlySet<nint>? wallpaperHandles = null)
    {
        nint foreground = Native.GetAncestor(Native.GetForegroundWindow(), 2);
        var result = new List<WindowSnapshot>();
        foreach (nint window in Native.TopLevelWindows())
        {
            string cls = Native.Class(window);
            // Show Desktop can raise the icon root above applications without minimizing them.
            // Windows below that desktop surface cannot occlude the wallpaper.
            if ((cls is "WorkerW" or "Progman") && Native.IsWindowVisible(window) &&
                Native.FindWindowEx(window, 0, "SHELLDLL_DefView", null) != 0) break;
            if (!Native.IsWindowVisible(window) || Native.IsIconic(window) || !Native.GetWindowRect(window, out var outer)) continue;
            bool cloaked = Native.DwmInt(window, 14, out int cloak, sizeof(int)) == 0 && cloak != 0;
            if (cloaked) continue;
            bool onCurrent = virtualDesktops is null || virtualDesktops.IsWindowOnCurrentVirtualDesktop(window, out bool current) != 0 || current;
            if (!onCurrent) continue;
            uint style = (uint)Native.GetWindowLongPtr(window, -16).ToInt64();
            uint ex = (uint)Native.GetWindowLongPtr(window, -20).ToInt64();
            var frame = Native.DwmRect(window, 9, out var extended, Marshal.SizeOf<Native.Rect>()) == 0 && !extended.Pixels.IsEmpty ? extended.Pixels : outer.Pixels;
            var client = default(PixelRect);
            if (Native.GetClientRect(window, out var local))
            {
                Native.MapWindowPoints(window, 0, ref local, 2); client = local.Pixels;
            }
            bool opaque = (ex & WindowStyles.Transparent) == 0;
            if ((ex & 0x80000) != 0)
                opaque &= Native.GetLayeredWindowAttributes(window, out _, out byte alpha, out uint flags) && (flags & 1) == 0 && ((flags & 2) == 0 || alpha == 255);
            // A rectangular custom region can be clipped exactly. Complex/per-pixel shapes are conservatively excluded.
            if (region != 0)
            {
                int kind = Native.GetWindowRgn(window, region);
                if (kind == 2 && Native.GetRgnBox(region, out var box) != 0)
                    frame = frame.Intersect(new(outer.Left + box.Left, outer.Top + box.Top, outer.Left + box.Right, outer.Top + box.Bottom));
                else if (kind is 1 or 3) opaque = false;
            }
            Native.GetWindowThreadProcessId(window, out uint pid);
            result.Add(new()
            {
                Handle = window.ToInt64(), ProcessId = pid, ClassName = cls, Frame = frame, Client = client, Style = style, ExtendedStyle = ex,
                Foreground = window == foreground, Maximized = Native.IsZoomed(window), Owned = Native.GetWindow(window, 4) != 0,
                Opaque = opaque, SystemSurface = SystemClasses.Contains(cls), Wallpaper = wallpaperHandles?.Contains(window) == true
            });
        }
        return result;
    }

    public DetectionSnapshot Analyze(IReadOnlyList<MonitorSnapshot> monitors, SuspensionSettings settings, IReadOnlySet<nint>? handles = null)
    {
        bool exclusive = settings.FullscreenDetectionEnabled && Native.SHQueryUserNotificationState(out int state) == 0 && state == 3;
        return WindowAnalyzer.Analyze(monitors, Capture(handles), settings.FullscreenToleranceDip, exclusive,
            settings.Mode == SuspensionMode.GlobalFullscreen && settings.FullscreenDetectionEnabled);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (region != 0) Native.DeleteObject(region);
        if (virtualDesktops is not null) { Marshal.FinalReleaseComObject(virtualDesktops); virtualDesktops = null; }
    }
}
