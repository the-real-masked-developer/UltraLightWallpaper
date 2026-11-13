# Windows acceptance procedure

Run in an approved Windows 11 desktop session. These tests are necessary before calling this application production-ready. Use the published directory, not the deeply nested intermediate build directory. Preserve ordinary work before testing sleep, display hotplug or restarting Explorer.

## Repeatable automated checks

```powershell
.\scripts\Build-Test.ps1
.\scripts\Make-TestVideo.ps1 -OutputPath .\artifacts\test-video.mp4 -Fps 30
.\scripts\Build-Test.ps1 -Native -Video .\artifacts\test-video.mp4
```

The first command validates Core policy, geometry and persistence. The native harness uses actual HWNDs, creates borderless and maximized windows on the real monitors, tests hide/minimize/restore and multiple concurrent fullscreen windows, then plays the test video on each monitor. It measures playing/suspended/resumed CPU and memory, verifies decoding stops while paused, verifies playback position and HWND preservation, and drives global suspension using actual window events. It fails rather than reporting a performance result if no frames decode. It writes JSON and diagnostics to `artifacts/test-results` and cleans up its windows/players.

The native fullscreen windows are test applications, not exclusive DXGI games. Their success is not proof that every game's fullscreen implementation is detected.

## Desktop, video and UI

1. Assign the 1440p 30 FPS sample to both displays. Turn automatic suspension off temporarily. Confirm moving video on both screens, with no audio session.
2. Show the desktop. Confirm icons are visible over the moving wallpaper. Click an icon, drag a desktop selection rectangle, open the desktop context menu, and use the taskbar. The wallpaper must never receive focus or appear in Alt+Tab.
3. On the portrait display select Cover. The entire display must be filled without black bars or distortion. Move both focal controls and reset to center. Compare Contain and Stretch, then restore Cover.
4. Close Settings. Confirm playback continues and the tray menu still works. Pause all, then resume all. Inspect that the same frame remains while paused; there should be no black flash on resume.
5. Reopen Settings → Performance. Change each suspension setting while videos are playing. No restart should be needed. Exit completely and restart; mode, threshold, delay, assignments and focal positions must persist.

## Three modes

For each test, keep a visible region of the other monitor's desktop unless the step deliberately covers it. Diagnostics lists coverage, qualifying fullscreen handles and each player's pause reason.

| Test | Expected result |
|---|---|
| Independent mode, maximized app on A | A pauses at threshold, B continues |
| Independent mode, maximized app on B | B pauses, A continues |
| Independent mode, both covered | Both pause independently |
| Gaming mode, fullscreen game on A | Both pause with GlobalFullscreen reason |
| Gaming mode, fullscreen game on B | Both pause with GlobalFullscreen reason |
| Gaming mode, borderless game | Both pause |
| Gaming mode, fullscreen video/browser F11 | Both pause |
| Gaming mode, ordinary framed maximized browser | No GlobalFullscreen reason; only the covered monitor pauses |
| Global occlusion mode, one monitor ≥95% covered | Both pause with GlobalOcclusion reason |
| Any mode, hidden/minimized app | It does not contribute fullscreen/coverage |
| Gaming mode, fullscreen detection unchecked | Independent coverage continues; no global fullscreen trigger |
| Automatic suspension disabled | Automatic holds clear immediately; manual pause remains honored |

## Transitions and contention

1. Alt+Tab repeatedly from a fullscreen game to a normal window and back. Test a game that minimizes on deactivation and one that remains visible.
2. Run two fullscreen applications, one per monitor. Close/minimize the first: both wallpapers stay globally paused. Close/minimize the last: the global reason clears after the configured resume delay; independent coverage may still keep one paused.
3. Move a large window around the 95% threshold. Coverage in the 92–95% hysteresis band must not cause oscillation. Uncover to below 92%, wait the resume delay, then recover.
4. During a pending resume, re-enter fullscreen. Playback must remain paused. Test a short notification/overlay above a game.
5. Enable manual Pause all, then exit every fullscreen application. The manual hold must remain. Clear manual pause while a fullscreen app is still present: the automatic fullscreen hold must remain.
6. Move a fullscreen application to a different Windows virtual desktop, switch back, and verify the state follows visible, uncloaked applications. Do not assume windows on inactive desktops count.
7. Disconnect the monitor hosting a fullscreen app. No stale monitor/window state may hold all playback forever. Reconnect and verify the stored assignment returns. Repeat with orientation/scaling/primary-monitor changes.

## Loop and range regression

Run `Build-Test.ps1 -Layout -LoopVideo <mp4> -SecondVideo <mkv> -FullDuration` with two displays. The harness observes timestamp wraparounds and continued displayed frames, rather than assuming a high frame count proves repetition. It verifies four short-segment loops, pause across several loop durations, two loops after resume, disabled-loop EOF, three native-EOF tail loops and two complete-file loops per monitor. Session identity, native surface identity and video-output generation must remain unchanged during normal loops.

In the UI, enter a start with three decimals, uncheck the file-end option, enter a later end, and apply. Test reversed/equal/out-of-file bounds: the apply button must remain unavailable and the explanation visible. Adjust draft values without applying and verify no player is recreated. Test timeline handles and the current-position buttons, then reopen the saved configuration. Resize all four pages to 640×560 and verify all actions remain reachable by scrolling and keyboard navigation.

## Recovery and soak

- Restart Explorer using the normal Windows workflow. Verify automatic reattachment behind icons and resumed media position. This test was not performed automatically against the user's running shell.
- Sleep/resume; lock/unlock; test a game that changes display resolution.
- Assign a missing file, invalid file, H.264 MP4, HEVC MP4, VP9 WebM and AV1 if supported by the actual driver/backend. Errors should be per-monitor and visible.
- Run at least 100 short video loops, 100 pause/resume transitions, 25 wallpaper replacements and a multi-hour soak. Check private bytes, native handles, GDI/USER resources and libVLC frame statistics for bounded growth.
- Test driver/device recovery only in a controlled environment. Do not intentionally reset the user's GPU as an automatic smoke test.

## Performance methodology

Use source files at 24, 25, 30 and 60 FPS. Warm up decoding for 10 seconds, then collect at least 60 seconds each for one 1440p wallpaper, two wallpapers, one paused, and both paused. Repeat the same video and UI visibility across comparisons. CPU is percent of total machine CPU capacity; on a six-core machine one fully occupied core is about 16.7%.

```powershell
.\scripts\Measure-Performance.ps1 -TargetProcessId <wallpaper-process-id> -Seconds 60 -OutputPath .\artifacts\playing.csv
```

In a separate console during the integration harness, the sampler can read its phase/PID file:

```powershell
.\scripts\Measure-Performance.ps1 -Seconds 90 `
  -PhaseFile .\artifacts\test-results\native-acceptance.phase.txt `
  -OutputPath .\artifacts\gpu-phases.csv
```

The Windows GPU counters are process-specific engine sums (3D, video decode and video processing), not a single total-GPU percentage. Counter paths may be localized. Use GPUView/PresentMon or Windows performance tools for frame pacing; libVLC displayed/lost frame counters alone do not prove smooth presentation. Report source codec/FPS, driver, OS, resolutions, scaling and sample duration. Discard runs where no frames were decoded, a player was recovering, or another test changed the workload. A zero-decode failed player is not a successful low-power result.
