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
