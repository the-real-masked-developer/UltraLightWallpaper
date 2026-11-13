# Verification — UltraLightWallpaper 1.1 — 12 November 2026

## Result

The 1.1 source build runs on the target Windows 11 desktop. Both original 4K videos completed real loops with continued decoded/displayed frames; normal looping retained player identity, native HWNDs and video-output generation. The three suspension policies remain implemented, and native fullscreen tests now include players that are actually decoding. The installed copy was rebuilt at its existing location with the user's assignments, crop, speed and suspension preferences retained; looping is enabled for both assigned displays.

## Evidence

The [public test summary](evidence/public-summary.json) keeps check names, results and aggregate measurements. Raw workstation transcripts are excluded from this publication.

- **70 Core tests passed**, zero failures/skips: `evidence/public-summary.json`. Includes all existing geometry, fullscreen/coverage, hysteresis, monitor topology and persistence tests, plus actual-duration range validation, millisecond values, old-schema compatibility and recovery-position clamping.
- **55 combined layout/loop checks passed**, zero failures: `evidence/public-summary.json`.
- **35 native detector/player checks passed**, zero failures: `evidence/public-summary.json`. Includes borderless fullscreen on either screen, maximized-window rejection, global coverage, independent policy, minimized/hidden windows, multiple fullscreen windows, last-window exit, decode suspension and surface retention.
- An additional **19 player checks passed** during three 60-second workload phases: `evidence/public-summary.json`.
- Release build completed without compiler warnings/errors. The self-contained release's `--diagnostics` smoke test exited 0 and produced its report.

The test machine is Windows build 26300, Intel i5-8600K (6 cores), RTX 4080. Connected displays: 2560×1440 primary and 1080×1920 portrait at negative X. D3D11VA decoder evidence was observed for the actual media.

## Real loop checks

| Scenario | Observed result |
|---|---|
| Segment 2.125–5.625 seconds, two different files, 1.2× / 1.5× | At least 4 returns per monitor; frames continued after wraps |
| Manual pause longer than a segment, then resume | Time/decoding stayed stopped; at least 2 further loops per monitor |
| Loop disabled | Both players reached segment EOF and remained accurately marked ended |
| Last 3.5 seconds through native file EOF | At least 3 loops per monitor, no player or surface recreation |
| Full 64.640-second MP4, primary, 1.5× | **8 complete loops** observed |
| Full 208.880-second MKV, left, 1.2× | **2 complete loops** observed |

The tests measure actual backward timestamp transitions plus additional displayed frames, rather than using a frame-count threshold as a proxy for looping. Normal runs had no EOF fallback rearm. Retained video-output generation is evidence against output recreation, but does not prove there can never be a single-frame visual discontinuity. Native codec logs still report some seek/clock warnings around short segments; frame-perfect cuts and gapless presentation for every format are not promised.

## Startup and interface

The first uncached libVLC initialization measured 18,645 ms. The subsequent cached harness starts measured 57 and 69 ms; the actual app restart measured 451 ms. UI-ready timestamps measured 1,099 / 1,188 ms without assigned videos and 1,793 ms with the user's two assignments. These timings begin at managed program entry; Windows security/reputation checks before entry are excluded.

Native initialization runs once on a worker task. Metadata parsing is asynchronous. Both source-build output and portable output carry a validated cache for the exact 321 native plug-in DLLs. Publish verifies hashes, lengths and timestamps. No Windows protection was disabled or altered.

The four pages passed bounds and no-horizontal-scroll checks at 640×560, 800×650 and 1200×900. Screenshots were inspected for wallpaper, range and performance pages; scrolling exposed the full range and image controls. A real UI edit applied 2.125–5.625 seconds and saved those exact values to the user configuration; the original full-file range was then restored. The UI has French labels, an accessible monitor selector, a two-handle timeline, editable start/end seconds, explicit range application, and immediate crop/speed/performance controls. Closing Settings preserves background playback; Application has an explicit quit command.

## Workload measurements and limits

Two 3840×2160, 25 FPS H.264 wallpapers, 60 seconds per phase:

| Phase | CPU, total machine capacity | Private memory at phase end |
|---|---:|---:|
| Playing | 0.585% | 600.6 MiB |
| Suspended | 0.351% | 433.2 MiB |
| Resumed | 2.261% | 595.7 MiB |

Decoded-frame counts and media time stopped during sustained suspension. CPU during suspension was about 40% below the initial playing phase. The resumed phase was noisier; these samples are a functional workload check on an actively used desktop, not a controlled benchmark or an idle-CPU guarantee.

The first external GPU query opened before the test process registered its GPU engines, so it captured no matching counters. The empty fields from that discarded capture are **missing evidence, not 0% utilization**. The sampler was corrected to wait for registration.

A separate capture against the already-running installed application had 15 valid GPU-engine counters per sample. Two different original 4K files at the user's 1.2×/1.5× speeds were sampled for 15 seconds playing and 15 seconds manually paused; the first three samples of each phase were discarded:

| Installed app, 12 retained samples per phase | Playing | Paused |
|---|---:|---:|
| GPU video-decode engine | 31.134% | 0.000% |
| GPU 3D engine | 0.135% | 0.041% |
| Process CPU, total machine capacity | 6.008% | 4.387% |

This confirms that suspension stops sustained hardware decoding and reduces rendering activity. The UI was open and the desktop in active use; the short CPU samples remain noisy and show nonzero paused backend/UI overhead. They are not an idle-efficiency guarantee and are not directly comparable to the different-file/speed 60-second harness phases. Aggregated evidence: `evidence/public-summary.json`. Automatic suspension and manual override were restored after capture.

## Earlier blocks and remaining acceptance

Initial 1.0 attempts and the first RID-specific 1.1 harness launch were rejected by Windows Application Control (`0x800711C7`). The public summary retains the successful final checks; raw historical machine diagnostics remain local. The normal source build and the documented .NET-hosted harness subsequently executed successfully; the final portable diagnostics smoke also passed. Unsigned delivery remains subject to the recipient's Windows policy.

Actual exclusive games, true game Alt+Tab behavior, virtual desktop transitions, unplug/replug during a game, Explorer restart, device reset and a multi-hour soak were not performed against the user's live machine. Fullscreen tests used real temporary Win32/WinForms windows. The two-file regression validates this reported loop failure, not all codecs or corrupt media. WorkerW integration remains undocumented; exclusive fullscreen detection remains a conservative cross-process heuristic. Use `ACCEPTANCE.md` for the remaining system-level checks before a public production release.
