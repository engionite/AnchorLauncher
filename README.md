<div align="center">

# ⚓ Anchor Launcher

A Minecraft launcher for people who got tired of the other ones.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0a7cff)
![Built with](https://img.shields.io/badge/built%20with-C%23%20%2B%20WPF-512bd4)
![Version](https://img.shields.io/badge/version-1.3.0-2ea043)
![License](https://img.shields.io/badge/license-source--available-lightgrey)

</div>

Anchor is a desktop launcher for **Minecraft: Java Edition** on Windows. It handles the usual
things — accounts, versions, mod loaders, downloads — and then keeps going into the parts most
launchers skip: Ely.by accounts alongside Microsoft, custom skins that work on offline servers, a
single search across Modrinth and CurseForge, updating every mod at once, exporting and importing
modpacks, importing your whole library from another launcher, and hosting a friend over the internet
with no server. It launches Bedrock for you too.

Free to download, and this is the source.

**[Full feature list](FEATURES.md)**  ·  **[Changelog](CHANGELOG.md)**

## Highlights

The short version. The [feature list](FEATURES.md) covers the rest.

- Microsoft **and** Ely.by accounts, switchable in a click, with tokens encrypted on your machine.
- Modrinth and CurseForge in one search, filtered to whatever instance you're installing into.
- Update all your mods at once, and let installs pull in the dependencies they need.
- Export an instance to a Modrinth `.mrpack`, or import one and have the instance built for you.
- Import instances — worlds, mods and configs — from Prism, PolyMC, MultiMC, CurseForge, Modrinth or the official launcher.
- Play with a friend over the internet: open to LAN, host through your router, share an address. Direct, no server, no account.
- Reclaim gigabytes by de-duplicating the mods and packs your instances share.
- When the game crashes, Anchor reads the log and offers a fix instead of handing you a stack trace.
- A 15-language interface that switches instantly, and a launcher that updates itself in place.

## Install

1. Download `AnchorLauncher.exe` from the [latest release](https://github.com/engionite/AnchorLauncher/releases/latest).
2. Run it. No setup needed. If you'd rather have Start-menu shortcuts, there's a full installer
   (`AnchorLauncherSetup.exe`) on the same page.

The download is self-contained, so nothing else needs to be installed. On first run, Windows
SmartScreen may warn that the app isn't code-signed yet — choose **More info → Run anyway**. After
that Anchor keeps itself up to date, so you won't need to come back here for new versions.

## Built with

C# on .NET 8, with a WPF interface written in XAML. Anchor ships no game files of its own; Minecraft
is downloaded from Mojang's servers with your own account.

## Building from source

Requires the .NET 8 SDK.

```
dotnet publish AnchorLauncher/AnchorLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Contact

Bug reports, questions or business enquiries: **me@engionite.online**, or open an issue.

## License

Source-available, all rights reserved. You're welcome to read the code and run the official builds.
You may not copy, modify, redistribute or reuse it in other projects. See [LICENSE](LICENSE).

## Credits

Built by [engionite](https://github.com/engionite) at Simplexity Development. © 2026.

Not affiliated with Mojang or Microsoft. *Minecraft* is a trademark of Mojang Synergies AB.
