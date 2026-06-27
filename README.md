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

## Official Package

```powershell
.\scripts\publish-velopack.ps1 -SkipPreviousDownload
```

The official distribution path publishes a self-contained `win-x64` app and builds Velopack setup/update artifacts in `artifacts\velopack`. Public releases should be created from tags named `vX.Y.Z` where `X.Y.Z` matches `Directory.Build.props`.

For manual distribution or smoke testing on another Windows x64 PC, send the single installer copied to:

```powershell
artifacts\installer\KeyForge-Setup.exe
```

For a production release, push the matching tag to GitHub. The GitHub Actions workflow builds, tests, packages Velopack artifacts, and uploads them to the GitHub Release.

## Fair Play / Anti-Cheat

KeyForge is a keyboard remapping tool. It does not bypass, disable, hide from, or modify anti-cheat systems.

Remaps and macros can still violate a game, server, tournament, or platform rule and may lead to warnings, kicks, account limits, or bans. Check each game's rules before enabling profiles or macros.

## Automatic Updates

Installed Velopack builds check the public GitHub Releases feed once per day by default. Users can also open Settings and click **Check for Updates** at any time. KeyForge asks before downloading an update and asks again before installing/restarting.

The release repository URL is stamped into the app during CI. For local update-feed testing, set:

```powershell
$env:KEYFORGE_UPDATE_REPOSITORY_URL = "https://github.com/<owner>/<repo>"
```

## Legacy MSI

```powershell
.\scripts\publish-installer.ps1
```

The WiX MSI project is retained as a legacy/dev artifact only. Existing MSI testers should uninstall that build once, then install the official Velopack setup.

For local MSI testing only:

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
