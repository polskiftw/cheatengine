# Clean Cheat Engine 7.5 build

This repository does **not** redistribute Cheat Engine's public installer.

Instead, GitHub Actions builds the Windows x64 Cheat Engine 7.5 executable directly from the official source tag:

- Source: `https://github.com/cheat-engine/cheat-engine/tree/7.5`
- Project: `Cheat Engine/cheatengine.lpi`
- Build mode: `Release 64-Bit`
- Toolchain: Lazarus 2.2.2 + FPC 3.2.2, matching upstream's documented build instructions

The workflow uploads a `CheatEngine75-clean-x64` artifact containing the freshly compiled `cheatengine-x86_64.exe` plus the support files from the official 7.5 source tree's `Cheat Engine/bin` directory.

## Terraria: Infinite Angler

This repo contains two old-school drop-in patchers for vanilla Terraria:

- **Option A — `terraria/infinite-angler-host` / InfiniteAnglerHost**: patch only the host PC's **`TerrariaServer.exe`**. Vanilla players connected through Host & Play can keep taking Angler quests; guests install nothing.
- **Option B — `terraria/infinite-angler` / InfiniteAngler**: patch an individual player's **`Terraria.exe`**. That player gets endless personal Angler quests regardless of who hosts.

Both use Terraria **1.4.5.8** as the initial reference target but are deliberately **not hard-locked to a version string**. They validate the live methods/fields they modify and refuse unfamiliar structures instead of guessing.

Option A uses only vanilla multiplayer semantics. After the server receives a normal Angler completion, it removes that player's completion name, temporarily borrows vanilla quest selection with broadcasting suppressed, sends the target assembly's own `MessageID.AnglerQuest` packet only to that player, then restores the server's shared Angler state.

Option A and Option B normally modify different executables and should be treated as separate mechanisms.

## Why

The Cheat Engine build exists to provide a normal CE executable capable of opening `.CT` tables without using the public bundled-offer installer. The Terraria patchers are separate and do not require Cheat Engine at runtime.

## Provenance

Each Cheat Engine artifact includes `BUILD-PROVENANCE.txt` with the exact upstream Git commit and the SHA-256 hash of the executable produced by that run.

Infinite Angler artifacts include `SHA256.txt` for their self-contained patcher executable.

## Build

Use GitHub Actions:

- **Build clean Cheat Engine 7.5** for the CE artifact.
- **Build Infinite Angler Host** for Option A (host-only server patch).
- **Build Infinite Angler** for Option B (per-PC client patch).

No installer is produced or executed for Cheat Engine itself. The only installers used by CE CI are the official Lazarus/FPC compiler installers referenced by Cheat Engine's own build documentation.
