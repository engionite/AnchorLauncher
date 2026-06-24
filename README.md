<div align="center">

# ⚓ Anchor Launcher

**A Minecraft launcher for people who got tired of the other ones.**

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0a7cff)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![Status](https://img.shields.io/badge/status-open%20beta-ff5a1f)
![License](https://img.shields.io/badge/license-source--available-lightgrey)

</div>

Anchor is a desktop launcher for **Minecraft: Java Edition** on Windows (and it'll launch **Bedrock** for you
too). It takes care of the tedious parts — accounts, versions, mod loaders, downloads — and then keeps going
into the parts most launchers leave out: Ely.by accounts, custom skins that render on offline servers, one
search box for two mod sites, **one-click mod updates**, **modpack export**, importing your whole setup
straight from other launchers, and an interface that speaks your language.

It's free. This is the source.

> **Open beta.** It's stable and in daily use, but you may still find rough edges — issues and feedback welcome.

---

## What you get

**Accounts that just work**
Sign in with **Microsoft** or **Ely.by** and keep both around at once — switch the active account with one
click. Tokens are encrypted on your own machine with Windows' built-in DPAPI, and your password is never
written to disk. Signed out? You can still play offline.

**Instances, the way they should be**
Spin up a separate, sandboxed copy of the game for every version or pack. Pick **Fabric, Forge, NeoForge or
Quilt**, give each instance its **own icon** (built-in set, or drop in your own image), and tell them apart
at a glance. Switch an instance to a different Minecraft version and **undo it** if it breaks. Clone, export,
track playtime, grid or list — your call.

**One marketplace, both worlds**
Search **Modrinth and CurseForge from the same box**. One-click install into any instance, with results
filtered to what's actually compatible. Install a mod that needs **Fabric API** (or Cloth Config, or
Architectury) and Anchor offers to pull in those dependencies — and their dependencies — automatically.
Before a modded world launches, it scans for mod conflicts and offers to fix them.

**Mods that stay current**
**Update every mod in one click** — Anchor identifies what you have by file hash, finds newer compatible
builds on Modrinth, and swaps them in. Thinking about jumping to a new Minecraft version? **Check first**
which of your mods are already ready and which ones are holding you back.

**Build packs, and bring your own**
**Export any instance to a Modrinth `.mrpack`** in one click — your mods become tidy download links and your
configs ride along — ready to share. Already have a pack? **Import a `.mrpack`** and Anchor builds the whole
instance for you: right loader, right mods, configs and all.

**Switch in from another launcher**
Moving over from somewhere else? The **Migrate** button scans your PC for **Prism, PolyMC, MultiMC,
CurseForge, Modrinth and the official launcher** and imports the instances you pick — worlds, mods and
configs included.

**Skins worth showing off**
Preview your skin in **real 3D** (drag to spin it), pick a free one from a built-in gallery, or upload your
own. With Ely.by, your custom skin even **shows up on `online-mode=false` servers** — the thing that usually
sends people hunting for sketchy mods.

**Launch it your way**
Standard vanilla launch, or flip on **optimized mode** for Aikar-style G1GC flags. Watch live RAM usage while
you play. When something crashes, Anchor reads the log, tells you in plain English what went wrong, and
offers a **one-click fix** (out of memory → more RAM, wrong Java → grab the right one, missing mod → find it).

**Speaks your language — all of it**
The interface is fully translated into **15 languages** and switches **instantly**, no restart:
English · Español · Русский · Українська · 中文 · Eesti · Deutsch · Français · Português · 日本語 · 한국어 ·
Polski · Italiano · Nederlands · Türkçe.

**Both editions, one launcher**
It's here mainly for Java, but Anchor will **launch Bedrock Edition** too — it detects the install and points
you to the Store if you don't have it yet. (Bedrock is a launch hand-off; the Java mod tools above are
Java-only, because Bedrock simply doesn't use Forge/Fabric `.jar` mods.)

**Shows up, stays signed in, updates itself**
Anchor puts a **Discord Rich Presence** card on your profile while you play, **updates itself in-app** the
moment a new version ships (no reinstalling from GitHub), and **remembers your session** so it stops randomly
asking you to log back in.

**The small stuff**
OLED-black theme, UI scaling, system-tray, "start with Windows," cloud backup of your worlds and screenshots
to a OneDrive/Google Drive folder you already have mounted (no extra sign-in), and settings that **save the
moment you change them** — no "Apply" button to forget.

---

## Why another launcher?

Plenty of launchers do accounts, versions and mods. Here's what Anchor does that they usually don't:

| | Most launchers | Anchor |
|---|---|---|
| **Ely.by accounts** | rarely | first-class, next to Microsoft |
| **Custom skins on offline servers** | no | yes |
| **Modrinth + CurseForge** | pick one | both, one search |
| **Update all mods** | hunt them down | one click, hash-matched |
| **Dependencies (Fabric API…)** | go find them | offered automatically |
| **Import from other launchers** | manual folder digging | scans Prism/CF/Modrinth/official |
| **Export a modpack** | rarely | one-click `.mrpack` |
| **Per-instance icons** | a few presets | full set **+ your own image** |
| **Crash help** | "here's the log" | reads it and offers a fix |
| **Languages** | a couple | **15, live-switchable** |
| **Settings** | Apply / Save dance | auto-save |

---

## Install

1. Grab `AnchorLauncher.exe` from the **[latest release](https://github.com/engionite/AnchorLauncher/releases/latest)**.
2. Run it. No installer, no setup wizard.

The standard build needs the free **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**.
There's also a self-contained build that bundles everything and needs no install.

> First launch may show a Windows SmartScreen prompt (the app isn't code-signed yet) — click **More info →
> Run anyway**.

---

## Building from source

Requires the .NET 8 SDK.

```bash
dotnet publish AnchorLauncher/AnchorLauncher.csproj -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

---

## License

**Source-available — all rights reserved.** You're welcome to read the code and run the official builds.
You may not copy, modify, redistribute, or reuse it in other projects. See **[LICENSE](LICENSE)**.

## Credits

Made by **[engionite](https://github.com/engionite)** · Anchor Analytics · © 2026 Simplexity Development.

Not affiliated with Mojang or Microsoft. *Minecraft* is a trademark of Mojang Synergies AB. Anchor ships no
game files — Minecraft is downloaded from Mojang's official servers with your own account.
