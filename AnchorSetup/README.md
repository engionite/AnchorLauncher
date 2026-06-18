# Anchor Launcher — Setup

The installer for Anchor Launcher. It's a small WPF wizard rather than an MSI, so the whole
experience — the step sidebar, the language picker, the progress screen — matches the launcher
itself instead of looking like a generic Windows installer.

## What it does

- Walks through five steps: **Welcome → License → Options → Install → Finish**.
- Lets you pick from **15 languages** up front. The choice re-skins the wizard instantly *and* is
  written into Anchor's settings, so the launcher opens in that language on first run.
- Shows the license and won't continue until it's accepted.
- Installs per-user into `%LOCALAPPDATA%\Programs\Anchor Launcher` — **no administrator prompt**.
- Creates Start-menu and desktop shortcuts, can start Anchor with Windows, and registers a proper
  entry (with a working uninstaller) under *Apps & features*.

## Where the launcher comes from

At install time the wizard fetches `AnchorLauncher.exe` from the latest GitHub release. For offline
or bundled installs it instead uses a local copy if one sits next to `AnchorLauncherSetup.exe`
(or wherever `ANCHOR_SETUP_PAYLOAD` points).

## Building

```powershell
dotnet publish AnchorSetup/AnchorSetup.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish-setup
```

The result is a single, self-contained `AnchorLauncherSetup.exe` that runs on any Windows 10/11
machine with no .NET install required. CI builds and attaches it to each tagged release
(see `.github/workflows/release.yml`).
