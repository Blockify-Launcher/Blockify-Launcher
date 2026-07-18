<div align="center">
  <img src="https://github.com/Blockify-Launcher/.github/assets/84909252/5da2f5de-d890-427b-a25c-a57dbb53065b"/>
  <h1>🟩 Blockify Launcher</h1>
  <p><b>A liquid-glass Minecraft launcher.</b> Native WPF shell, a full HTML/CSS interface rendered inside the window, and a one-click modpack manager.</p>
</div>

![Banner](https://github.com/Blockify-Launcher/.github/assets/84909252/47206b7a-edc5-4b4c-b368-9e8e6634ecce)

<div align="center">

[![Build and Deploy WPF Application](https://github.com/Blockify-Launcher/Blockify-Launcher/actions/workflows/build-and-deploy.yml/badge.svg)](https://github.com/Blockify-Launcher/Blockify-Launcher/actions/workflows/build-and-deploy.yml)
![Platform](https://img.shields.io/badge/platform-Windows-6BBF3B?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)
![UI](https://img.shields.io/badge/UI-WebView2%20%C2%B7%20Liquid%20Glass-5EB8E6?style=flat-square)
![Lang](https://img.shields.io/badge/i18n-RU%20%C2%B7%20UA%20%C2%B7%20EN-F2C14E?style=flat-square)

</div>

---

## ⛏️ What is it

Blockify is a Minecraft launcher with a twist: the entire interface is **real HTML/CSS** — the exact "liquid glass" mockup — rendered inside a native WPF window through **WebView2**, and wired to the launch engine over a thin C#↔JS bridge. You get pixel-perfect design (backdrop-blur, custom fonts, smooth dropdowns) *and* native window behavior, launching, and file management.

> Windows only for now. Linux / macOS are on the roadmap.

---

## ✨ Features

### 🎨 Interface
- **Liquid-glass UI** — the mockup runs 1:1 (real `backdrop-filter`, Unbounded / Manrope / JetBrains Mono embedded).
- Frameless window with a custom title bar — drag, min/max/close, edge-resize, themed scrollbars.
- Unified **glass dialogs** (confirm / alert / errors) — no mismatched native popups.
- A live **download panel** with per-step progress (files `N/Total`, %) that never blocks the launcher.

### 📦 Modpacks (Modrinth)
- **Search & install** modpacks from the Modrinth catalog — query, MC-version filter, sort, loader chips.
- One-click **.mrpack install**: loader + Minecraft version + all mods + overrides.
- Loaders: **Fabric · Quilt** (via meta APIs) and **Forge · NeoForge** (official headless installer).
- **Isolated instances** — every pack lives in its own folder (`blockify-packs/<slug>`), with its own mods / config / saves, never touching your main game.
- Resilient downloads: **SHA-1 verified**, retried, and **resumable** (re-install only fetches what's missing).
- Manage installed packs: play, open folder, open mods, repair, remove.

### 👤 Accounts
- **Microsoft** sign-in (official servers, skins, Realms) — no password ever touches the launcher.
- **Offline** profiles with proper offline UUIDs (`OfflinePlayer:` scheme) — mods like Essential just work.
- Local **skin library** for offline profiles (upload PNG, assign, live face preview).

### 🧰 Utilities
- **Versions** — install any vanilla version (download-only), release/snapshot/old filters.
- **Mod updates** — scan `mods/`, match on Modrinth by hash, update in place.
- **Worlds & backups** — list saves, zip backups, one-click restore (auto-snapshot before overwrite).
- **Screenshots** — in-app gallery with a built-in **editor**: crop, rotate, draw, zoom, save / copy / delete.
- **News** — live Mojang + custom feed.
- Settings: RAM, Java path, Aikar flags, Discord RPC, favorite server, "close launcher after game starts", and more.

---

## 🧱 Tech stack

| Layer | Tech |
|------|------|
| Shell | .NET 8 · WPF · WindowChrome |
| UI | WebView2 (Chromium) · HTML / CSS / vanilla JS |
| Bridge | `WebMessageReceived` ↔ `PostWebMessageAsJson` |
| Launch engine | `BlockifyLib` (CmlLib-style, submodule) |
| Services | Modrinth API · Mojang manifest · Fabric/Quilt/Forge/NeoForge installers |

---

## 🚀 Build & run

```bash
# Requirements: .NET 8 SDK, Windows, WebView2 Runtime (preinstalled on Win11)
git clone --recurse-submodules https://github.com/Blockify-Launcher/Blockify-Launcher.git
cd Blockify-Launcher

dotnet build BlockifyLauncher.csproj -c Debug
dotnet run  --project BlockifyLauncher.csproj
```

> Already cloned without submodules? Run `git submodule update --init --recursive`.

---

## 🗂️ Project layout

```
WebUI/                     # the entire interface (index.html + app.js + fonts)
Core/
  Modrinth/                # catalog search, .mrpack installer, mod-update checks
  News/                    # Mojang + custom news feed
MVVM/Views/Window/
  MainWindow.Web.cs        # C#↔JS bridge
  MainWindow.Features.cs   # screenshots · worlds · skins
  MainWindow.Packs.cs      # installed-pack registry + launch
  MainWindow.Forge.cs      # headless Forge/NeoForge installer
libs/BlockifyLib/          # launch engine (submodule)
```

---

## 🛣️ Roadmap

- [x] Liquid-glass UI on WebView2
- [x] Modpack manager — Fabric / Quilt / Forge / NeoForge, isolated instances
- [x] Screenshot editor · worlds & backups · offline skins
- [ ] Per-pack mod management (enable/disable/update inside an instance)
- [ ] In-game skins for offline profiles (CustomSkinLoader)
- [ ] Linux & macOS

---

<div align="center">
  <img src="https://github.com/Blockify-Launcher/.github/assets/84909252/8f2b49c8-20df-4fb5-9a3c-b98bb773860f"/>

  🎵 Music for the soul — [listen](https://youtu.be/ESb3ad-1lJE?si=Q2SByRH-E4HANvkH)

  <sub>Built on pure enthusiasm. If you like it, drop a ⭐</sub>
</div>
