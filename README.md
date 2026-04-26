# GammaShift

Display brightness, contrast, and vibrance switcher for Windows. Switch profiles instantly with numpad hotkeys. Works in fullscreen, borderless, and windowed games.

## How to Use

Run `build.bat` to generate `GammaShift.exe`, then launch it. A tray icon appears. Press **Numpad 1-9** (or **Shift+1-9**) to switch between 9 profiles. Right-click the tray icon for the profile editor.

On first run, a one-time UAC prompt sets a registry key to enable full gamma range. After that, the app runs without admin.

## Features

- NVIDIA Digital Vibrance, AMD Saturation, GDI gamma fallback for Intel
- Auto-brightness: switches profiles based on screen darkness
- Multi-monitor support
- Anti-cheat safe: no DLL injection, no memory access, no game hooks

## Auto-Brightness

Set it up once, profiles switch automatically based on how dark the scene is.

1. Enable **Debug Overlay** from the tray. Top-left shows live screen brightness (0-255).
2. Note the value in a dark scene (cave, bunker) and in a bright one.
3. Open **Edit Profiles**, set `BrMin/BrMax` per profile to cover those values with no gaps.
4. Disable Debug Overlay, enable **Auto-Brightness**. Done.

Two configurable delays in the editor: **Brighten delay** (200ms default, fast when scene goes dark) and **Dim delay** (2000ms default, slower when scene gets bright so brief light flashes don't snap back).

## Why?

I've been playing [ARC Raiders](https://store.steampowered.com/app/1808500/ARC_Raiders/) and some parts of the game are very dark. Every brightness tool I found was a sketchy forum exe, so here we are with a sketchy GitHub exe instead. You're welcome.
