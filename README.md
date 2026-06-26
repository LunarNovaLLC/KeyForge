# KeyForge

KeyForge is a Windows desktop app for per-game keyboard remapping profiles. It provides a WPF visual keyboard editor, JSON-backed profiles, active-window profile switching, real low-level keyboard hooks, `SendInput` output, short macros, a tray menu, and an emergency disable hotkey.

## Build And Run

```powershell
dotnet build KeyForge.sln
dotnet run --project src/KeyForge.App/KeyForge.App.csproj
```

Profiles are stored under `%AppData%\KeyForge\profiles`, and settings are stored at `%AppData%\KeyForge\settings.json`.

## Test

```powershell
dotnet test KeyForge.sln
```

The tests cover profile validation, JSON round-tripping, active profile matching, macro sequencing, injected-input filtering, emergency disable, and simple remap suppression.

## Package

```powershell
.\scripts\publish-installer.ps1
```

The script publishes a self-contained `win-x64` app to `artifacts\publish\KeyForge` and builds the unsigned MSI from `installer\KeyForge.Installer`.

## Update Installed App

For day-to-day testing, use the update script instead of uninstalling first:

```powershell
.\scripts\update-installed.ps1
```

That command bumps the patch version, rebuilds the MSI, stops a running KeyForge instance, and runs the MSI as an in-place upgrade. To pick an exact version:

```powershell
.\scripts\update-installed.ps1 -Version 0.1.2
```

To only build the upgrade MSI without installing it:

```powershell
.\scripts\update-installed.ps1 -NoInstall
```

## Manual MVP Check

1. Open Notepad.
2. In KeyForge, click **Select Running Window** and choose the Notepad window.
3. Leave **Match this window title too** off for normal exe-based matching, or turn it on if you want only that exact titled window.
4. Click `Alt` on the visual keyboard.
5. Capture `Ctrl+B` as a combo and save.
6. Focus Notepad and press left Alt.
7. Confirm Notepad receives `Ctrl+B` and the original Alt is blocked.
8. Focus another app and confirm Alt behaves normally.
7. Assign `F1` to a macro: `1`, wait `50ms`, `2`, wait `50ms`, `3`.
8. Confirm `Ctrl+Shift+F12` pauses all remapping and releases held output keys.
