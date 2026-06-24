# Changelog

Every release of Anchor Launcher, newest first. Dates are when each version shipped.
Anchor updates itself in-app, so you're usually already on the latest one.

## 1.3.1 — 24 June 2026

- **Self-update is much more reliable.** The file swap now happens from a small helper that runs after the launcher has closed, so antivirus can't lock the new build mid-update. The "couldn't update, download it from GitHub" fallback should be rare from here on.
- **Fixed the import-from-another-launcher hang.** It no longer follows junction or symlink loops (CurseForge and Modrinth use those), tolerates files it can't read, and shows real per-file progress. The Import button hides while it's working.
- **Play with a Friend now works with dedicated servers too.** You can type the port directly — it's auto-filled when an Open-to-LAN world is detected — and every step shows clear status. The Host button hides while connecting so it can't be triggered twice.

## 1.3.0 — 24 June 2026

- **Reclaim Storage.** A new tool behind the disk icon on the Instances page. It finds the mods, shaders and resource packs that several instances share and keeps just one copy of each on disk, linking the rest to it. Ten instances running the same 500 MB shaderpack go from 5 GB down to 500 MB. It only ever touches downloaded content — your configs, worlds and options are never linked.
- **Play with a Friend.** Open a world to LAN in Minecraft, click Host, and Anchor forwards the port through your router (UPnP) and gives you an address to send a friend. They join from Multiplayer → Direct Connection. It's a direct connection between the two of you, with no server and no account in between. If your router won't allow it, Anchor tells you why instead of silently failing.

## 1.2.0 — 24 June 2026

- **Export a modpack.** Right-click an instance to save it as a Modrinth `.mrpack`. Mods that live on Modrinth become small download links; your configs and anything local are packed in directly.
- **Check mod compatibility before a version jump.** When switching an instance to a newer Minecraft version, see at a glance which mods already have a build for it and which ones don't yet.
- **Dependencies now bring their own dependencies.** Installing a mod that needs Architectury, which in turn needs Cloth Config, offers the whole chain at once.
- Fixed the annoying random sign-outs between sessions. Anchor now refreshes your token quietly at startup instead of dropping you back at the login screen.

## 1.1.0 — 23 June 2026

- **One-click mod updates.** Anchor recognises your installed mods by their file hash, looks up newer compatible builds on Modrinth, and swaps them in.
- **Dependency prompts.** Install a mod that needs Fabric API and Anchor offers to add it for you.
- **Bring your setup over from another launcher.** The Migrate button scans for Prism, PolyMC, MultiMC, CurseForge, Modrinth and the official launcher, and imports the instances you choose — worlds, mods and configs included. Accounts stay put; they're encrypted per launcher and can't be moved.

## 1.0.9 — 21 June 2026

- News posts are clickable, opening a detail view that links to the matching GitHub release.
- Tidier scrollbars in the Minecraft version lists.
- Clearer Download / GitHub buttons on the Discord card.

## 1.0.8 — 21 June 2026

- Self-update is sturdier: it retries the file swap when antivirus briefly locks the freshly downloaded build, retries the download itself on a network hiccup, and logs the reason if anything still goes wrong.
- The update download is roughly half the size now, thanks to compression.
- It checks for updates every two minutes instead of every five.

## 1.0.7 — 21 June 2026

- Discord Rich Presence went live — while Anchor is open it shows on your profile with the current version.

## 1.0.6 — 20 June 2026

- Added Discord Rich Presence and live update notices that appear without restarting.
- Forge and NeoForge install failures now explain what went wrong, in your language, instead of dumping a raw error.
- Security pass: removed an API key from the source tree and moved it to build-time injection.

## 1.0.5 — 19 June 2026

- The previous launcher now closes itself the instant an update finishes, so you never end up with two windows open.

## 1.0.4 — 19 June 2026

- Near-instant update detection, new posts in the news feed, and more dialogs translated.

## 1.0.3 — 19 June 2026

- Optimized Launch actually applies its JVM flags now, shader packs activate in-game, and you can copy your in-game settings from one instance to another. Also fixed the uninstaller and a stale version number on the About screen.

## 1.0.2 — 19 June 2026

- In-app updates: a built-in prompt downloads and installs new versions, so there's no more reinstalling from GitHub by hand.

## 1.0.1 — 18 June 2026

- Cut down false positives in the mod-conflict scanner, fixed the Settings page scrolling, and translated the right-click and edit-instance menus.

## 1.0.0-beta — 18 June 2026

First public build. Microsoft and Ely.by sign-in side by side; sandboxed instances on Fabric, Forge, NeoForge and Quilt; a single search box across Modrinth and CurseForge; a 3D skin viewer with custom skins that work on offline-mode servers through Ely.by; crash logs read back in plain language with one-click fixes; cloud backup of worlds and screenshots; a fully translated 15-language interface; and one-click Bedrock launch. Shipped with its own installer.
