# Automatic resume after Windows lock / display wake

This patch registers `GUID_SESSION_DISPLAY_STATUS` with `RegisterPowerSettingNotification` on the existing hidden STA window, in addition to WTS session and system suspend/resume notifications.

When the user session or display is unavailable, the existing suspension policy pauses video playback. When Windows reports that the session/display has returned, a one-shot 1-second timer coalesces duplicate wake signals and gives Explorer and D3D11 time to recover.

The engine then re-enumerates monitors, retains the previous per-monitor playback positions, rebuilds the video players and native windows under the validated background WorkerW, and re-applies user/manual/fullscreen pause conditions. A temporary shell failure is retried by the existing 5-second health timer once active; there is no extra continuous polling.

## Windows acceptance checklist

1. Start video playback on a primary monitor and optional portrait secondary monitor.
2. Press Win+L, unlock later, and check for automatic restart behind icons without pressing `Relancer le moteur`.
3. Let Windows power off the displays using its timeout, then wake them. Repeat several times with the settings window hidden.
4. Repeat with a fullscreen game covering one monitor, and with manual pause active. Both pause conditions must remain in force.
5. Inspect the existing application log for `Rebuilding wallpaper players after Windows session/display wake.` and diagnostics `WakeRecoveryPending`.

This patch needs a Windows compilation and actual GPU/display testing before merging. Switching off a particular monitor using its physical power button does not necessarily send the Windows session-display notification.

## DisplayPort power button / monitor hot-unplug

Some monitors report a physical disconnect when their power button is pressed. Windows then re-enumerates the desktop; existing Direct3D11 windows can be invalid, even if libVLC still reports an initialized player.

- `WM_DISPLAYCHANGE` / `WM_DPICHANGED`: queue one delayed screen rebuild instead of reconciling against transient geometry.
- `WM_DEVICECHANGE` / `DBT_DEVNODES_CHANGED`: probe the monitor list after 1 second; rebuild only when the monitor snapshot actually changes (avoids restarting on every USB device event).
- Existing 5-second health checks also detect missed changes, even when the last configured display is absent. No busy-loop or decoding is performed for disconnected monitors.
- Persisted assignments remain untouched. When a monitor with the *same stable interface path ID* reconnects, its original video and preserved playback position return.
- A bounded three-attempt retry handles temporarily unavailable desktop hosts.
- The code does not change the default Windows background image while a monitor is actually disconnected or the desktop host is unavailable.

### Manual test for physical power cycling

1. Run a video on both monitors. Turn off the primary display with its own power button; verify the disconnect sound and that remaining connected monitors keep their assignments.
2. Turn the primary display on again. Wait for Windows to finish identifying it; confirm UltraLightWallpaper automatically recreates playback behind the icons.
3. Repeat with Windows lock screen and ordinary display sleep. Verify playback resumes only when the session/display is active.
4. Inspect diagnostics before and after: monitor `Id`, player count, `ShellStatus`, `DisplayRecoveryRetries`, and video state. If the returned monitor gets a different `Id`, configuration reassignment may be needed; do not silently assign wallpapers to an unknown monitor.
5. Verify that routine USB connect/disconnect does not interrupt video playback when the monitor topology is unchanged.
