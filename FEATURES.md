# Anchor Launcher — full feature list

A complete tour of what Anchor does. For a short pitch see the [README](README.md);
for what changed in each release see the [changelog](CHANGELOG.md).

Everything here is Windows-only and built around **Minecraft: Java Edition**, with a
one-click hand-off to **Bedrock** as well.

---

## Accounts

- Sign in with a **Microsoft** account or an **Ely.by** account, and keep several signed in at once. Switching the active one is a single click.
- Tokens are encrypted on your own machine with Windows' DPAPI. Your password is never written to disk.
- Sessions are remembered between launches — Anchor refreshes your token in the background instead of asking you to sign in again every day.
- No account handy? You can still launch and play offline.

## Instances

Each instance is a self-contained copy of the game with its own mods, worlds, config and options. Nothing leaks between them.

- Create instances on **Vanilla, Fabric, Forge, NeoForge or Quilt**, pick the exact loader version, and Anchor installs everything it needs.
- Give every instance its own **icon** — choose from the built-in set or drop in your own image — so they're easy to tell apart in the grid or list.
- Set Java arguments and RAM globally, then override them per instance for the heavy packs that need more.
- **Switch an instance to a different Minecraft version**, and if it goes wrong, undo the switch and get the previous state back.
- **Clone** an instance to test a risky change without touching your stable copy.
- Export an instance to a `.zip`, import one back, and track how long you've played each.

## Mods, shaders and resource packs

- One search box covers **both Modrinth and CurseForge**. Results are filtered to what actually fits the instance you're installing into.
- Install with one click into any instance.
- **Update all your mods at once.** Anchor identifies what you have by file hash, finds newer compatible builds on Modrinth, and replaces them.
- **Automatic dependencies.** Install a mod that needs Fabric API, Cloth Config or Architectury and Anchor offers to add them — including dependencies of dependencies.
- **Compatibility check before a version jump.** Thinking of moving a world from 1.20 to 1.21? See which mods are ready and which ones haven't been updated yet, before you commit.
- A pre-launch **conflict scan** flags real problems and offers to fix them, tuned to avoid crying wolf over things that are actually fine.
- Mod snapshots let you roll back the contents of an instance's mods folder if an install breaks something.

## Building and sharing packs

- **Export any instance to a Modrinth `.mrpack`** in one click. Mods that exist on Modrinth become tidy download references; your configs and any local files are bundled in as overrides. Personal data like worlds and options is left out.
- **Import a `.mrpack`** and Anchor builds the whole instance for you — correct loader, mods, configs and all.

## Moving in from another launcher

- The **Migrate** button scans your PC for the official Minecraft launcher plus **Prism, PolyMC, MultiMC, CurseForge and Modrinth**, lists the instances it finds (with the loader and version it detected), and imports the ones you pick — worlds, mods and configs included.
- Accounts aren't copied. They're encrypted per launcher with no portable form, so you sign in again afterwards rather than Anchor pretending it moved them.

## Playing with a friend

- Open a world to LAN in Minecraft, click **Host**, and Anchor forwards the port through your router using UPnP, then hands you an address to share.
- Your friend joins from Multiplayer → Direct Connection. The connection runs **directly between the two of you** — no relay server, no account, nothing in the middle.
- Where a router blocks automatic port forwarding (UPnP turned off, or carrier-grade NAT), Anchor says so plainly instead of leaving you guessing.

## Skins

- Preview your skin in **real 3D** and drag to spin it.
- Upload your own, or pick a free one from a built-in gallery.
- With Ely.by, your custom skin shows up even on `online-mode=false` servers — the thing that usually sends people looking for sketchy mods.

## Launching and fixing problems

- Standard launch, or flip on **Optimized** for an Aikar-style G1GC flag set.
- Watch live RAM use while you play.
- When the game crashes, Anchor reads the log and tells you in plain language what went wrong, then offers a **one-click fix**: out of memory points you at more RAM, the wrong Java version offers to fetch the right one, a missing library offers to find it.

## Storage

- **Reclaim Storage** finds mods, shaders and resource packs shared across instances and keeps a single copy of each, hard-linking the duplicates to it. On a modded setup that can be many gigabytes back. It only touches downloaded content — never config, saves or options.

## Bedrock

- Anchor detects whether **Minecraft Bedrock** is installed and launches it for you, or sends you to the Store if it isn't. It's a launch hand-off — the Java mod tools above don't apply to Bedrock, which doesn't use `.jar` mods.

## Interface

- The whole interface is translated into **15 languages** and switches instantly, no restart: English, Español, Русский, Українська, 中文, Eesti, Deutsch, Français, Português, 日本語, 한국어, Polski, Italiano, Nederlands and Türkçe.
- Dark theme and an OLED-black variant, adjustable UI scale, a system-tray option and "start with Windows."
- A built-in news feed, and settings that save the moment you change them — no Apply button to forget.

## Staying current

- Anchor updates itself. When a new version ships it offers to download and install it in place, closes the old copy on its own, and you keep playing — no reinstalling from GitHub.
- It also puts a **Discord Rich Presence** card on your profile while it's open, with quick links to download and to the project.

## Cloud backup

- Copy your worlds and screenshots into a OneDrive or Google Drive folder you already have synced. No extra sign-in — Anchor just writes to the folder.

---

## The handful you won't find elsewhere

If you only skim one section, this is it:

- Ely.by accounts as first-class citizens, next to Microsoft.
- Custom skins that actually render on offline-mode servers.
- Modrinth and CurseForge in one search instead of one or the other.
- Update every mod, and pull in their dependencies, in a click.
- Import your whole library from Prism, CurseForge, Modrinth or the vanilla launcher.
- Play with a friend over the internet with no server and no third party.
- Reclaim gigabytes by de-duplicating shared files across instances.
- Crash logs that come back as a fix, not a wall of text.
