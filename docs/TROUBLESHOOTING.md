# Troubleshooting

## Application Control blocks the executable or assembly

This engineering package is unsigned. The development host returned `0x800711C7` and Code Integrity event 3077 for the apphost and later managed application assembly. A successful compile is not approval to run on a protected machine. Use the organization's approved signing/deployment procedure or an approved development environment. Do not turn off Smart App Control, Defender, or application-control policy. No security settings were changed during this work.

## Wallpaper stays detached

Open Diagnostics. A valid host must be an Explorer-owned WorkerW below the icon view and large enough to contain the monitor. The program refuses a floating-window fallback. Try “Refresh displays” or “Restart wallpaper engine”. Save the topology report for a Windows-build-specific investigation. Desktop icon visibility, Explorer replacement shells and third-party desktop customization can affect hosting.

## Video fails immediately

Check that the file is still present and that the complete runtime directory was extracted. `libvlc/win-x64` and `plugins` must remain beside the application. libVLC 3 plugin loading can fail at legacy 260-character paths even though .NET itself supports long paths. The application checks this and asks for a shorter installation path. The packaged publish directory avoids the deep development output paths.

Check `engine.log` for the underlying libVLC error. The error logger retains the first 16 native error messages per five-second interval to avoid obscuring distinct startup failures while bounding storms. For a short development trace, set `ULW_VERBOSE_NATIVE=1` in the launching process's environment; unset it after diagnosis. Verbose logging is not intended for continuous release use.

Try a known-good H.264 MP4. Hardware acceleration is requested, not assumed. A missing decoder or unsupported profile may fall back to software or fail. “Awaiting decoder evidence” is not evidence of hardware decoding. Diagnostics' frame counters must advance before drawing a performance conclusion.

## Wallpaper remains paused

Inspect the reason. Manual, per-monitor pause, inactive session, fullscreen, coverage and resume-delay holds are distinct. “Resume all” only clears Manual. In fullscreen mode, independent occlusion remains active once the final fullscreen application clears. An ordinary maximized application can therefore keep its own wallpaper paused without triggering global fullscreen suspension.

Minimize or close the remaining qualifying application, or disable automatic suspension to isolate detection. A visible fullscreen application on a nonforeground monitor intentionally keeps every player paused. A fully obscured fullscreen application is not considered presented. For unusual captioned or dialog-style fullscreen applications, the conservative classifier may miss fullscreen; global occlusion is a selectable alternative, not an automatic mode substitution.

## Resume flickers near the coverage threshold

Default 95% coverage triggers pause; clearing requires coverage to fall to 92% or lower, followed by a 650 ms delay. Raise the delay if an application repeatedly changes visibility. Increasing detection tolerance affects edge matching, not the coverage threshold. Excessive tolerance may classify near-fullscreen borderless windows as fullscreen.

## Configuration recovery

Settings are written with atomic replacement. If parsing fails, the previous backup is tried and the UI reports recovery. Damaged input is retained as `.invalid` when the recovered configuration is saved. If saving fails due to file permissions or disk errors, the UI says changes are active but unsaved. Missing videos and disconnected monitor assignments are retained for repair.

## Portrait bars or video rotation

Confirm Cover, not Contain. Reset crop center. Save Diagnostics with video dimensions and monitor bounds. Metadata rotation and sample aspect ratio are included in layout, but arbitrary corrupt metadata and codec-specific output orientation need real-file validation. A rotation/scaling change uses physical monitor coordinates and recomputes placement without recreating the player.

## Looping and resource use

The application uses native input-repeat on the same MediaPlayer. It does not stop/recreate a player on every EndReached. Format, keyframe and demux seek behavior can still create a small loop gap; frame-perfect loops are not guaranteed. Each monitor has a separate decoder so one can pause independently. The same-file dual-monitor case is not decoded once and shared. Cover's clipped child can allocate a larger render surface on portrait screens. Both are deliberate initial correctness tradeoffs and must be measured before further optimization.

Version 1.0 incorrectly passed a negative native repeat count; upgrade to 1.1 if a video freezes permanently at its end. Check **Lire en boucle** on each monitor. For a custom range, make sure **Appliquer la séquence** was clicked. If looping is intentionally disabled, diagnostics and the UI show that the video has ended, rather than reporting it as playing.

## Slow first launch

Version 1.1 initializes libVLC in the background and includes its validated plug-in cache. The window should remain usable while the engine prepares. If a native cache cannot be written in a read-only installation, playback still works, but a missing cache may need rebuilding at later starts. Windows may separately delay or reject an unsigned binary before the program runs; the `Application UI ready` timing starts at program entry and does not measure that OS security stage.
