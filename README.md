# UltraLightWallpaper

A Windows 11 x64 video wallpaper engine built from a clean project. It includes independent monitor suspension, global fullscreen suspension, and global coverage suspension as core features.

**Version 1.1 — boucle corrigée, séquences et interface adaptable.** Les deux vidéos réelles ont passé plusieurs boucles complètes sur les deux écrans. Les coupes début/fin, la suspension et la reprise sont également testées. Voir [les changements et leur utilisation](docs/CHANGES-1.1.md) et [les résultats de vérification](docs/VERIFICATION.md).

## Sur un autre ordinateur

Le [guide de démarrage](docs/DEMARRAGE-RAPIDE.md) explique comment extraire le ZIP autonome et choisir les vidéos sur le nouveau PC. Les vidéos et les réglages personnels ne font pas partie du dépôt.

## Run

Extract the complete `UltraLightWallpaper-win-x64.zip` to a short path, then open `UltraLightWallpaper.exe`. The portable release includes the .NET runtime, libVLC, and its x64 plugins. No administrator privileges or separate runtime installation are intended for ordinary use. The package is unsigned; application-control environments may require an approved signing/deployment process. Do not disable Windows security protections to run it.

Choose a display in **Fonds d’écran**, select a local video, and leave scaling on **Remplir l’écran · recadrer** for a portrait monitor. In **Séquence vidéo**, set the start and end with the handles or millisecond-precision numeric fields; uncheck **Jusqu’à la fin du fichier** to enter an end. Click **Appliquer la séquence** once the cut is ready. **Lire en boucle** repeats that segment. The fields accept precise time values; actual presentation follows the source frame timing and demuxer, not an editing/export pipeline.

Closing Settings leaves the engine in the notification area. The tray menu opens Settings, pauses/resumes all, restarts the engine, or exits completely. **Application → Quitter l’application** also exits completely. The settings pages scroll vertically at small sizes; no restart is needed for changes.

In **Performances → Suspension automatique**, select:

1. **Écrans indépendants** — default; each wallpaper pauses at 95% coverage.
2. **Tout mettre en pause si une application est en plein écran** — a qualifying fullscreen application on any connected display pauses every player. After the final qualifying fullscreen window disappears, the engine returns to independent coverage behavior.
3. **Tout mettre en pause si un écran est suffisamment couvert** — any display reaching the coverage threshold pauses every player, including coverage by ordinary maximized windows.

Controls include automatic suspension on/off, coverage threshold, fullscreen detection on/off in fullscreen mode, DPI-scaled tolerance, resume delay, verification interval, and a manual pause override. Changes apply immediately and save after a 400 ms debounce; exit flushes pending changes. “Resume all” clears the manual override; it does not override an active automatic suspension condition. Global manual pause is session-only; individual monitor pause is persisted.

## Build and test

Requirements: Windows 11 x64 and .NET SDK **10.0.404** (or a compatible patch accepted by `global.json`). No Visual Studio installation is needed.

```powershell
.\scripts\Build-Test.ps1
.\scripts\Publish.ps1
```

The exact compile command is:

```powershell
dotnet build UltraLightWallpaper.slnx -c Release
```

The source-build executable is `src\UltraLightWallpaper.Windows\bin\Release\net10.0-windows\UltraLightWallpaper.exe`. Use the published package for runtime testing: deeply nested build paths can exceed libVLC 3's plugin path limit. `Publish.ps1` defaults to a sibling `UltraLightWallpaper-win-x64` directory and creates the ZIP beside it.

The native integration harness creates temporary fullscreen windows on the connected displays and closes them automatically. It does not terminate Explorer, change monitor settings, or change startup settings.

```powershell
.\scripts\Make-TestVideo.ps1 -OutputPath .\artifacts\test-video.mp4
.\scripts\Build-Test.ps1 -Native -Video .\artifacts\test-video.mp4
.\scripts\Build-Test.ps1 -Layout -LoopVideo C:\Videos\one.mp4 -SecondVideo C:\Videos\two.mkv -FullDuration
```

Native tests run through the installed .NET host, using a shorter staging directory. If Application Control blocks the assembly, that test is blocked; it is not a pass. Use an approved environment/signing process for the remaining acceptance checks.

## Technology and structure

The application uses .NET 10, C# 14, WinForms for the settings/tray only, direct Win32/DWM interoperability, LibVLCSharp **3.10.2**, and libVLC **3.0.24**. Wallpaper video goes directly to native child HWNDs with Direct3D 11 output and D3D11VA requested. No managed video-frame callbacks or CPU image resizing are used. Hardware decoding is reported only after libVLC emits decoder evidence; unsupported codecs may fall back to software or fail with a diagnostic.

```text
src/UltraLightWallpaper.Core/       Geometry, window classification, suspension policy, settings
src/UltraLightWallpaper.Windows/    Native integration, playback, event tracking, tray, settings UI
tests/UltraLightWallpaper.Tests/    Deterministic xUnit regression tests
tests/UltraLightWallpaper.Integration/  Native windows and real-player acceptance harness
scripts/                           Build, publish, test-video and performance tools
docs/                              Architecture, acceptance steps, troubleshooting, verification
assets/                            Original application icon
third_party/                       License texts, upstream sources and source manifest
artifacts/test-results/             Machine-readable test evidence (generated)
```

Settings: `%LOCALAPPDATA%\UltraLightWallpaper\settings.json`; backup: `.bak`; damaged input retained as `.invalid` after recovery/save. Logs rotate at approximately 2 MiB, retaining one backup. A missing file produces a per-monitor error without removing its assignment. Disconnected monitor settings remain stored. A different monitor interface identifier is treated as a new display, requiring reassignment rather than guessing a match.

Useful switches:

```text
--hidden                   Start in the notification area
--settings <path>          Isolated configuration for testing
--diagnostics <file>       Export monitor/shell diagnostics without starting wallpapers
--exit-after <seconds>     Bounded test run
--prepare-native-cache     Generate VLC's cache in an approved build environment
```

## Limits and documentation

- [Architecture and API references](docs/ARCHITECTURE.md)
- [Reproducible acceptance procedure](docs/ACCEPTANCE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Verification report](docs/VERIFICATION.md)
- [Dependency notices and source locations](THIRD-PARTY-NOTICES.md)

Exclusive fullscreen and borderless fullscreen cannot be identified perfectly across every application. WorkerW integration uses an undocumented Explorer message and validates the discovered topology; it deliberately refuses to show an ordinary floating wallpaper window when no safe host is found. See the architecture document for exact detection rules, transparency limitations, and playback tradeoffs.
