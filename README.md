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

## Why?

I've been playing [ARC Raiders](https://store.steampowered.com/app/1808500/ARC_Raiders/) and some parts of the game are very dark. Every brightness tool I found was a sketchy forum exe, so here we are with a sketchy GitHub exe instead. You're welcome.
