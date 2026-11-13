# Architecture

## Decisions

**.NET 10 + WinForms + native HWND playback.** WinForms provides a modest, event-driven settings/tray surface. It does not render wallpaper frames. Its 500 ms position/status timer stops when hidden. The application runs one STA message loop, with libVLC's native media threads handling video work. The shared LibVLC instance initializes on a worker task, so native plug-in discovery cannot block the settings window. The release carries a validated native plug-in cache; missing caches are generated once in that background initializer.

**libVLC 3.0.24 with LibVLCSharp 3.10.2.** One shared LibVLC instance owns independent MediaPlayers. Direct3D 11 output and D3D11VA are requested; audio and subtitle pipelines are disabled. This chooses mature demuxing, codec fallback and presentation timing over a new custom Media Foundation/D3D implementation whose device sharing, sample scheduling, loops and device-loss paths would need significantly more validation. Media Foundation could reduce deployment size, but codec availability and a custom renderer increase correctness risks for this first implementation. WPF composition was not chosen because the video can be presented directly to HWNDs. DirectComposition/custom D3D swap chains are possible future backend work, not an implemented hidden dependency.

The program does not claim zero-copy in every codec/driver combination. It does not copy decoded frames through managed memory; libVLC determines its internal hardware transfer behavior. Hardware status is aggregate evidence from one or more players, not a per-monitor proof of GPU usage. Actual video decode counters must be measured.

## Components and ownership

`WallpaperContext` owns the dispatcher, Engine, tray, settings form and persistence timer. `Engine` owns the window hooks, timers, inspector, sessions and lazy video backend. Each `PlaybackSession` owns its Media, MediaPlayer and two native windows. libVLC callbacks marshal back to the STA before calling libVLC again. Disposal unsubscribes callbacks, stops the player, releases media/native resources, then destroys child windows. Late callbacks observe the disposed flag.

The pure Core assembly has no Win32, UI or player dependency. It accepts immutable snapshots and monotonic time. This makes coverage, classification and pause policy deterministic and independently testable.

## Playback ranges and looping

Local Media parsing is asynchronous and bounded. `PlaybackRange` validates the configured start and nullable end against the actual duration before adding `:start-time` and `:stop-time` to the media. The fields accept milliseconds; presentation remains dependent on demuxer/frame timing. Unknown-duration files may play untrimmed, but unvalidated trims are rejected. Parse completion and disposal are ordered so that the Media object is not freed while its parse task still detaches callbacks.

libVLC 3.0.24 accepts `input-repeat` in the range 0..65535; negative values disable repetition. The enabled loop uses 65535 and VLC's internal seek to start-time. A rare EndReached fallback stops/rearms the same MediaPlayer only after the native counter is exhausted or a native EOF occurs. It is posted to the UI dispatcher, gated by effective suspension, and never invoked synchronously from a native callback. Ordinary loops do not call Stop or rebuild the session. Ended non-looping media retains an accurate ended status even during policy reevaluation.

Changing a draft start/end does not alter playback. Applying the range or changing file/loop recreates that monitor's session once. Speed and crop changes update the retained player. Decoder-error retries remain bounded; invalid range settings are not retried automatically.

## Desktop hosting and coordinates

The manifest and application startup request Per-Monitor DPI Awareness V2. Screen and DWM rectangles are physical pixels; monitor positions may be negative and the primary display need not be at the origin. Monitor interface paths from `EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)` identify assignments.

On attach/recovery the engine sends a bounded `0x052c` request to Progman, then discovers the actual Explorer topology. It supports a WorkerW sibling below the icon view in a common Progman, and the classic top-level background WorkerW below the icon-bearing root. It verifies process ownership, class names, relative Z order and host coverage. No fixed WorkerW index is used. If validation fails it keeps playback detached and reports the problem.

Each monitor gets a disabled, nonactivating `WS_CHILD` surface parented directly to the validated background host. A nested child receives libVLC video. Children are kept at the bottom of their parent's Z order. The wallpaper is never a topmost or Alt+Tab window. Hit testing is transparent; keyboard and mouse handling in libVLC are disabled. Desktop icons remain owned by Explorer above the host.

Cover computes an aspect-preserving video rectangle large enough to fill the monitor, with a focal offset inside a clipped parent. Contain centers an aspect-preserving smaller rectangle. Stretch sets a target aspect ratio. libVLC/D3D performs scaling; there is no CPU resize. Anamorphic sample aspect ratios and 90-degree video orientations are considered. Cover's oversized video child can use a larger GPU render surface than a source-crop shader would; that is a documented memory/processing tradeoff. A future directly cropped output path should be benchmarked before replacing this simple geometry.

## Detection

Top-level windows are sampled in Z order. Eligibility requires visible, nonminimized, noncloaked windows on the current virtual desktop, above the desktop icon surface. The latter prevents Show Desktop's raised shell surface from leaving behind stale occlusion. The inspector uses DWM extended frame bounds and client rectangles mapped to physical screen space. Wallpaper windows and shell surfaces are excluded.

Opaque coverage uses the exact union of clipped rectangles, never the sum of overlapping areas. A sweep across unique X edges merges vertical spans, using 64-bit area. Rectangular custom regions are clipped. Irregular regions, unknown per-pixel layered alpha, color-key transparency and `WS_EX_TRANSPARENT` are excluded conservatively. A layered window with known whole-window alpha 255 and no color key may count. Arbitrary DirectComposition acrylic/transparent content without corresponding window metadata cannot be determined precisely; a nonlayered rectangular application may be overestimated. The taskbar is excluded because it can be translucent or auto-hidden. The full monitor is the wallpaper surface, so small displays with unusually large taskbars may need a lower coverage threshold.

Fullscreen classification is separate from coverage:

1. Reject ineligible, translucent, dialog, child, tool, nonactivating and known shell windows. An owned popup can be the actual fullscreen player surface, so ownership alone is not an exclusion.
2. Match both client and frame edges to one monitor, within a configurable tolerance in DIP scaled by that monitor's DPI. Matching only a large area is insufficient. A spanning window that does not match one monitor is not classified as fullscreen.
3. Require no caption; a thick-frame maximized window does not qualify as borderless. An ordinary framed maximized browser remains a coverage candidate, not a fullscreen candidate.
4. Require some presentation area not fully covered by opaque windows above it. Foreground information labels/strengthens evidence, but a visible fullscreen application on a nonforeground monitor still qualifies.
5. Treat `QUNS_RUNNING_D3D_FULL_SCREEN` as extra exclusive-fullscreen evidence for an eligible foreground application. Do not treat `QUNS_BUSY`, quiet hours or presentation settings as proof.

The result is a set of qualifying HWND/monitor pairs, recomputed from live windows. Removing one member never clears other members. Small overlays do not discard an underlying fullscreen game; minimizing, cloaking, moving to another virtual desktop or completely obscuring it removes its qualification. A captionless ordinary program deliberately using identical fullscreen geometry is observationally indistinguishable from a borderless fullscreen app. An unusual custom-style game may be missed. Modern fullscreen optimizations can present an application through the borderless path even if its own UI says “exclusive”. No cross-process DXGI swap-chain inspection or injection is attempted.

## Policy and timing

| Mode | Global trigger | Behavior without global trigger |
|---|---|---|
| Independent monitors | None | Pause each monitor at its own coverage threshold |
| Global fullscreen | At least one qualifying fullscreen app, if fullscreen detection is enabled | Independent coverage |
| Global occlusion | Any connected monitor at the coverage threshold, including a monitor without an assigned video | Resume each player once the global hold clears |

Fullscreen global suspension has priority over per-monitor coverage. Default threshold is 95%; a latched monitor must fall to at most 92% before it starts clearing. Default resume delay is 650 ms. A fresh trigger cancels the pending resume. A single global delay gate keeps all monitors paused together; per-monitor gates handle independent coverage. Manual global pause, individual persisted pause, and inactive-session pause are independent reasons. An automatic resume cannot cancel those reasons. Mode, enable, threshold and timing changes invalidate old policy gates immediately.

Out-of-context WinEvents cover foreground, minimize/restore, move/size end, desktop switch, create/destroy/show/hide/reorder, state/location, and DWM cloaking transitions. Child/caret/control noise is filtered. Events schedule one evaluation after 100 ms; repeated events do not restart and starve the timer. A configurable 1000 ms fallback catches missed style/exclusive transitions. A one-shot resume timer expires at the actual policy deadline. There is no frame-rate window polling. Tracking timers stop when there are no assigned/enabled wallpapers; automatic detection timers stop when automatic suspension is disabled.

`MediaPlayer.SetPause(bool)` is only called when effective playback state changes (or the player first becomes ready). It retains the MediaPlayer, Media, native windows, decoder resources and last frame. Pausing requests that libVLC stop input/decoding/presentation; some backend bookkeeping can still occur. Global policy does not call Stop, seek or recreate players. No claim of zero GPU activity is made without measurement.

## Recovery and persistence

Display/DPI notifications refresh monitors, update geometry and dispose disconnected sessions without deleting settings. TaskbarCreated and a five-second health check detect missing/reordered shell hosts. Explorer recovery rebuilds native windows and restores saved playback time where possible; this recovery legitimately recreates sessions, unlike suspension. Decoder errors have at most three automatic session retries at health-check intervals, then require explicit restart or a setting change. File errors remain visible per monitor. libVLC owns its lower-level device-loss recovery; driver resets still require acceptance testing.

JSON configuration uses a same-directory flushed temporary file and atomic replacement. The previous good file is a backup. Corrupt/unsupported input produces an explicit recovery message; subsequent save preserves it as `.invalid` without destroying the good backup. Logs rotate with bounded retention. Startup registration only changes HKCU Run when the user toggles the setting.

## Primary references

- [WinEvent hooks and callback threading](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [DWM extended frame bounds and cloaking](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Shell user notification states and exclusive Direct3D evidence](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state)
- [Virtual desktop manager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- [Cross-process parenting and DPI caveats](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
- [libVLC instance ownership, disposal and callback guidance](https://docs.videolan.me/libvlcsharp/docs/best_practices.html)
- [LibVLCSharp 3.10.2 source](https://github.com/videolan/libvlcsharp/tree/3082c09ca955131c705fc4767fde03e9c4a14999)

WorkerW hosting is undocumented shell behavior. The observed topology and successful HWND validation on one Windows build are not a guarantee across Windows versions.
