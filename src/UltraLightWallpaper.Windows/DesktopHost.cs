using System.ComponentModel;
using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

public sealed record DesktopTopology(long Parent, long IconRoot, long IconView, string Strategy)
{
    public bool IsValid
    {
        get
        {
            if (!Native.IsWindow((nint)Parent) || Native.Class((nint)Parent) != "WorkerW" ||
                Native.Class((nint)IconView) != "SHELLDLL_DefView" || Native.GetParent((nint)IconView) != (nint)IconRoot) return false;
            Native.GetWindowThreadProcessId((nint)Parent, out uint parentProcess);
            Native.GetWindowThreadProcessId((nint)IconRoot, out uint iconProcess);
            if (parentProcess != iconProcess) return false;
            // Revalidate relative Z order, including handle reuse after an Explorer restart.
            nint anchor = Native.GetParent((nint)Parent) == (nint)IconRoot ? (nint)IconView : (nint)IconRoot;
            for (nint next = Native.GetWindow(anchor, 2); next != 0; next = Native.GetWindow(next, 2))
                if (next == (nint)Parent) return true;
            return false;
        }
    }
}

public static class DesktopHost
{
    public static DesktopTopology? Discover(bool requestWorker)
    {
        nint progman = Native.FindWindow("Progman", null);
        if (progman == 0) return null;
        if (requestWorker)
        {
            // Undocumented shell protocol: bounded, and used only on attach/recovery, never per scan.
            Native.SendMessageTimeout(progman, 0x052c, 0x0d, 1, 2, 1000, out _);
            Native.SendMessageTimeout(progman, 0x052c, 0, 0, 2, 1000, out _);
        }
        Native.GetWindowThreadProcessId(progman, out uint explorerPid);
        var top = Native.TopLevelWindows();
        for (int i = 0; i < top.Count; i++)
        {
            nint root = top[i];
            if (Native.Class(root) is not ("Progman" or "WorkerW")) continue;
            nint icons = Native.FindWindowEx(root, 0, "SHELLDLL_DefView", null);
            if (icons == 0) continue;
            // Recent Windows 11 builds can keep a background WorkerW under the same Progman.
            for (nint child = Native.GetWindow(icons, 2); child != 0; child = Native.GetWindow(child, 2))
                if (IsBackgroundWorker(child, explorerPid) && Native.GetParent(child) == root)
                    return new(child.ToInt64(), root.ToInt64(), icons.ToInt64(), "WorkerW sibling below icon view");
            // Classic shell: icon-bearing root followed in Z order by a separate background WorkerW.
            for (int j = i + 1; j < top.Count; j++)
                if (IsBackgroundWorker(top[j], explorerPid))
                    return new(top[j].ToInt64(), root.ToInt64(), icons.ToInt64(), "Top-level WorkerW below icon root");
        }
        return null; // Never fall back to a floating/topmost desktop-sized window.
    }
    private static bool IsBackgroundWorker(nint window, uint explorerPid)
    {
        Native.GetWindowThreadProcessId(window, out uint pid);
        return pid == explorerPid && Native.Class(window) == "WorkerW" && Native.IsWindowVisible(window) &&
            Native.FindWindowEx(window, 0, "SHELLDLL_DefView", null) == 0 && Native.GetWindowRect(window, out var r) && !r.Pixels.IsEmpty;
    }

    public static string Describe()
    {
        var lines = new List<string>();
        foreach (var w in Native.TopLevelWindows().Where(w => Native.Class(w) is "Progman" or "WorkerW"))
        {
            Native.GetWindowRect(w, out var rect);
            lines.Add($"0x{w:X} {Native.Class(w)} visible={Native.IsWindowVisible(w)} rect={rect.Pixels} parent=0x{Native.GetParent(w):X}");
            Native.EnumChildWindows(w, (child, _) =>
            {
                string cls = Native.Class(child);
                if (cls is "WorkerW" or "SHELLDLL_DefView" or "SysListView32") lines.Add($"  0x{child:X} {cls} parent=0x{Native.GetParent(child):X}");
                return true;
            }, 0);
        }
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class WallpaperWindow : NativeWindow, IDisposable
{
    public WallpaperWindow(nint parent, PixelRect bounds, bool visible)
    {
        CreateHandle(new CreateParams
        {
            Caption = "UltraLightWallpaper surface", Parent = parent,
            Style = unchecked((int)(0x40000000u | 0x02000000 | 0x04000000 | (visible ? 0x10000000u : 0))),
            ExStyle = 0x08000000 | 0x80 | 0x20,
            X = bounds.Left, Y = bounds.Top, Width = bounds.Width, Height = bounds.Height
        });
        Native.EnableWindow(Handle, false);
    }
    public void SetBounds(int x, int y, int width, int height)
    {
        if (!Native.SetWindowPos(Handle, 1, x, y, width, height, 0x0010 | 0x0200)) throw new Win32Exception(); // HWND_BOTTOM, NOACTIVATE, NOOWNERZORDER
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = -1; return; } // HTTRANSPARENT
        if (m.Msg == 0x21) { m.Result = 3; return; } // MA_NOACTIVATE
        if (m.Msg == 0x14) { m.Result = 1; return; } // Preserve last frame during pause/resume.
        if (m.Msg == 0x0f)
        {
            nint hdc = Native.BeginPaint(Handle, out var paint);
            if (hdc != 0) Native.FillRect(hdc, ref paint.Rectangle, Native.GetStockObject(4)); // BLACK_BRUSH, clipped around video children.
            Native.EndPaint(Handle, ref paint); m.Result = 0; return;
        }
        base.WndProc(ref m);
    }
    public void Dispose() { if (Handle != 0) DestroyHandle(); }
}
