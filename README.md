# GammaShift

Open source display brightness, contrast, and vibrance switcher for gaming. Built for any game with dark areas — caves, night cycles, horror maps, you name it. Works in fullscreen, borderless, and windowed modes.

Switch display profiles instantly with numpad hotkeys — no alt-tabbing.

## Features

### Display Control
- **9 brightness profiles** — each with independent gamma, contrast, and digital vibrance
- **Numpad hotkeys (1-9)** — works in fullscreen via low-level keyboard hook
- **Shift + 1-9** — alternative hotkeys for laptops without numpad
- **NVIDIA Digital Vibrance** — via NvAPI (same as NVIDIA Control Panel)
- **AMD Saturation** — via ADL (same as AMD Radeon Software)
- **GDI fallback** — brightness/contrast works on any GPU (Intel, etc.)
- **Multi-monitor support** — select which display to adjust

### Auto-Brightness
- **Automatic profile switching** based on screen darkness
- **5-zone measurement** — center (weighted 2x), plus 4 corners
- **500ms sampling interval** — fast enough to react to scene changes
- **Per-profile brightness ranges** — e.g., Profile 1 for bright areas (10-255), Profile 3 for caves (0-3.9)
- **Debug overlay** — visualize the 5 measurement zones on screen

### Game Mute
- **Per-process audio muting** — mute only the game, keep Discord/Spotify playing
- **Numpad 0** to toggle
- **Auto-detects foreground game** — or falls back to known process names

### UI & Settings
- **System tray app** — runs silently in background
- **Profile Editor** — edit all 9 profiles with live preview (gamma, contrast, vibrance, name, brightness range)
- **Calibration Guide** — step-by-step instructions to set up auto-brightness
- **Toast notifications** — on-screen popup when switching profiles
- **Auto-Start with Windows** — optional, user-controlled
- **Bilingual** — English and German
- **Portable** — single EXE, no installation, no dependencies
- **~64 KB** — tiny footprint

## Works With Any Game

GammaShift adjusts your display, not the game. It works with every game on your monitor:

- **Extraction shooters** — ARC Raiders, Escape from Tarkov, The Cycle, Hunt: Showdown
- **Horror** — Phasmophobia, Lethal Company, GTFO, Amnesia, Outlast
- **Survival** — Rust, DayZ, The Forest, Grounded, Subnautica
- **Souls-like** — Elden Ring, Dark Souls, Lies of P
- **Co-op** — Deep Rock Galactic, GTFO, Left 4 Dead 2
- **Any game with dark areas, night cycles, or caves**

## How It Works

GammaShift uses standard Windows and GPU driver APIs. It does **not** touch the game in any way.

| What | API | Same as... |
|------|-----|------------|
| Brightness/Contrast | `SetDeviceGammaRamp` (GDI) | Monitor OSD brightness slider |
| NVIDIA Vibrance | NvAPI (`nvapi64.dll`) | NVIDIA Control Panel > Digital Vibrance |
| AMD Saturation | ADL (`atiadlxx.dll`) | AMD Radeon Software > Saturation |
| Game Mute | Windows Core Audio COM | Windows Volume Mixer |
| Auto-Brightness | `BitBlt` + `GetPixel` (GDI) | Windows screenshot API |
| Hotkeys | `WH_KEYBOARD_LL` hook | Standard global hotkey method |

## Anti-Cheat Safe

GammaShift is safe to use with all anti-cheat systems (EAC, BattlEye, Vanguard, etc.):

- **No DLL injection** — does not attach to game processes
- **No memory reading/writing** — never touches game memory
- **No game file modification** — doesn't change any game files
- **No process hooking** — doesn't hook game functions
- **No rendering pipeline hooks** — no DirectX/Vulkan interception

It operates at the **display driver level** — the same level as your monitor's brightness controls. Anti-cheat systems don't monitor display adjustments because that would break Windows accessibility features.

## Quick Start

1. Download `GammaShift.exe` (or build from source)
2. Run it — a tray icon appears
3. Press **Numpad 1** = Normal, **Numpad 2** = Bright, **Numpad 3** = Brighter
4. Right-click tray icon for settings, profile editor, and more

### Default Profiles

| Key | Profile | Gamma | Contrast | Vibrance | Auto-Brightness Range |
|-----|---------|-------|----------|----------|-----------------------|
| Num 1 | Normal | 1.0 | 1.0 | 50 | 10 — 255 (bright scenes) |
| Num 2 | Bright | 1.5 | 1.1 | 60 | 4 — 9.9 (dim scenes) |
| Num 3 | Brighter | 2.0 | 1.1 | 70 | 0 — 3.9 (caves/dark) |
| Num 4-9 | Custom | 1.0 | 1.0 | 50 | disabled |
| Num 0 | Toggle Game Mute | — | — | — | — |

### Setting Up Auto-Brightness

1. Enable **Auto-Brightness** from the tray menu
2. Open your game and go to a bright area — hover over the tray icon to see the brightness value
3. Go to a dark area (cave, night) — note that value too
4. Open **Edit Profiles** and set brightness ranges for each profile
5. GammaShift will now automatically switch profiles based on scene brightness

## Build from Source

Requires only the .NET Framework that ships with Windows 10/11. No Visual Studio needed.

```cmd
build.bat
```

Or manually:

```cmd
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /target:winexe /platform:anycpu /win32manifest:src\app.manifest /out:GammaShift.exe src\GammaShift.cs
```

## First Launch

On first run, GammaShift sets a registry key to enable full gamma range:

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 256
```

This requires a one-time UAC prompt. It's the same key used by display calibration tools (X-Rite, Datacolor) and is required for `SetDeviceGammaRamp` to work beyond the default limited range. The app then runs without admin privileges.

## System Requirements

- Windows 10 or 11
- .NET Framework 4.0+ (pre-installed on all modern Windows)
- NVIDIA or AMD GPU recommended (for vibrance/saturation control)
- Intel/GDI fallback for brightness and contrast only

## Uninstall

1. Right-click tray icon → Exit
2. Delete `GammaShift.exe` and `GammaShift.cfg`
3. If you enabled Auto-Start, remove `GammaShift` from `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

Display settings are restored to defaults on exit.

## License

MIT — do whatever you want with it. See [LICENSE](LICENSE).
