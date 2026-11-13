using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace UltraLightWallpaper.Windows;

internal sealed class EventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public EventHookHandle(nint value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle() => Native.UnhookWinEvent(handle);
}

/// <summary>Out-of-context WinEvents, dispatched on the owning STA message loop.</summary>
internal sealed class WindowEvents : NativeWindow, IDisposable
{
    private readonly Native.WinEventProc callback;
    private readonly List<EventHookHandle> hooks = [];
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private bool disposed;
    private bool sessionUnavailable, powerSuspended;
    public event Action? Changed;
    public event Action? DisplaysChanged;
    public event Action? ShellRestarted;
    public event Action<bool>? SessionChanged;
    public WindowEvents()
    {
        CreateHandle(new CreateParams { Caption = "UltraLightWallpaper events", ExStyle = 0x08000080 });
        callback = OnWinEvent;
        foreach (var (min, max) in new (uint, uint)[]
        {
            (3,3), (0x0b,0x0b), (0x16,0x17), (0x20,0x20),
            (0x8000,0x8004), (0x800a,0x800b), (0x8017,0x8018)
        })
        {
            nint handle = Native.SetWinEventHook(min, max, 0, callback, 0, 0, 0);
            if (handle == 0) Log.Write("WinEvent hook unavailable; periodic verification remains active.", new Win32Exception());
            else hooks.Add(new(handle));
        }
        if (!Native.WTSRegisterSessionNotification(Handle, 0)) Log.Write("Session notifications unavailable.", new Win32Exception());
    }
    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || (eventType >= 0x8000 && (objectId != 0 || childId != 0))) return;
        // Ignore child-location noise (video output, carets, controls), but keep destroyed HWNDs.
        if (window != 0 && Native.IsWindow(window) && Native.GetAncestor(window, 2) != window) return;
        Changed?.Invoke();
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == taskbarCreated) ShellRestarted?.Invoke();
        else if (m.Msg is 0x007e or 0x02e0) DisplaysChanged?.Invoke();
        else if (m.Msg == 0x02b1)
        {
            int code = (int)m.WParam;
            if (code is 2 or 4 or 6 or 7) sessionUnavailable = true;
            else if (code is 1 or 3 or 5 or 8) sessionUnavailable = false;
            SessionChanged?.Invoke(sessionUnavailable || powerSuspended);
        }
        else if (m.Msg == 0x0218)
        {
            if ((int)m.WParam == 4) { powerSuspended = true; SessionChanged?.Invoke(true); }
            else if ((int)m.WParam is 7 or 18) { powerSuspended = false; SessionChanged?.Invoke(sessionUnavailable); DisplaysChanged?.Invoke(); }
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var hook in hooks) hook.Dispose();
        hooks.Clear();
        Native.WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
        GC.KeepAlive(callback);
    }
}
