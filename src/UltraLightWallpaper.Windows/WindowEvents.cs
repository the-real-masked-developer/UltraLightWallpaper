using System.ComponentModel;
using System.Runtime.InteropServices;
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
    private static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private nint displayRegistration;
    private bool disposed;
    private bool sessionUnavailable, powerSuspended, displayUnavailable, previouslyInactive;
    public event Action? Changed;
    public event Action? DisplaysChanged;
    // Some DisplayPort hot-plug events precede (or replace) WM_DISPLAYCHANGE.
    public event Action? DevicesChanged;
    public event Action? ShellRestarted;
    public event Action<bool>? SessionChanged;
    public event Action? RecoveryRequested;
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
        if (!Native.WTSRegisterSessionNotification(Handle, 0)) Log.Write("Session notifications unavailable.", new Win32Exception(Marshal.GetLastWin32Error()));
        Guid displayGuid = SessionDisplayStatus;
        displayRegistration = Native.RegisterPowerSettingNotification(Handle, ref displayGuid, 0); // DEVICE_NOTIFY_WINDOW_HANDLE
        if (displayRegistration == 0) Log.Write("Display power notifications unavailable.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || (eventType >= 0x8000 && (objectId != 0 || childId != 0))) return;
        // Ignore child-location noise (video output, carets, controls), but keep destroyed HWNDs.
        if (window != 0 && Native.IsWindow(window) && Native.GetAncestor(window, 2) != window) return;
        Changed?.Invoke();
    }
    private bool IsInactive => sessionUnavailable || powerSuspended || displayUnavailable;
    private void NotifyAvailability(bool mightNeedVideoRecovery = false)
    {
        bool inactive = IsInactive;
        bool becameActive = previouslyInactive && !inactive;
        if (inactive != previouslyInactive)
        {
            previouslyInactive = inactive;
            SessionChanged?.Invoke(inactive);
        }
        // Native D3D11 video output can be lost while the window handles remain valid.
        if (!inactive && (becameActive || mightNeedVideoRecovery)) RecoveryRequested?.Invoke();
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == taskbarCreated) ShellRestarted?.Invoke();
        else if (m.Msg is 0x007e or 0x02e0) DisplaysChanged?.Invoke(); // WM_DISPLAYCHANGE / WM_DPICHANGED
        else if (m.Msg == 0x0219 && (int)m.WParam == 7) DevicesChanged?.Invoke(); // WM_DEVICECHANGE / DBT_DEVNODES_CHANGED
        else if (m.Msg == 0x02b1) // WM_WTSSESSION_CHANGE
        {
            int code = (int)m.WParam;
            if (code is 2 or 4 or 6 or 7) sessionUnavailable = true;
            else if (code is 1 or 3 or 5 or 8) sessionUnavailable = false;
            NotifyAvailability(code is 1 or 3 or 5 or 8);
        }
        else if (m.Msg == 0x0218) // WM_POWERBROADCAST
        {
            int code = (int)m.WParam;
            if (code == 4) // PBT_APMSUSPEND
            {
                powerSuspended = true;
                NotifyAvailability();
            }
            else if (code is 7 or 18) // PBT_APMRESUMESUSPEND / PBT_APMRESUMEAUTOMATIC
            {
                powerSuspended = false;
                NotifyAvailability(true);
                DisplaysChanged?.Invoke();
            }
            else if (code == 0x8013 && m.LParam != 0) // PBT_POWERSETTINGCHANGE
            {
                // POWERBROADCAST_SETTING: GUID (16 bytes), DataLength (4), DWORD data.
                Guid setting = Marshal.PtrToStructure<Guid>(m.LParam);
                if (setting == SessionDisplayStatus && Marshal.ReadInt32(m.LParam, 16) >= sizeof(int))
                {
                    int state = Marshal.ReadInt32(m.LParam, 20); // 0 off, 1 on, 2 dimmed
                    if (state is >= 0 and <= 2)
                    {
                        bool wasOff = displayUnavailable;
                        displayUnavailable = state == 0;
                        NotifyAvailability(wasOff && !displayUnavailable);
                    }
                }
            }
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var hook in hooks) hook.Dispose();
        hooks.Clear();
        if (displayRegistration != 0) Native.UnregisterPowerSettingNotification(displayRegistration);
        Native.WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
        GC.KeepAlive(callback);
    }
}
