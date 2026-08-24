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

- **Option A — `terraria/infinite-angler-host` / InfiniteAnglerHost**: patch only the **Host & Play PC**. Any vanilla player connected to that host can keep taking Angler quests with no daily cooldown. Guests install nothing.
- **Option B — `terraria/infinite-angler` / InfiniteAngler**: patch each individual PC. That player gets endless personal Angler quests regardless of who hosts.

Both use Terraria **1.4.5.8** as the initial reference target but are deliberately **not hard-locked to a version string**. They validate the live methods/fields they modify and refuse unfamiliar structures instead of guessing.

Option A uses only vanilla multiplayer semantics: after the server receives the normal Angler completion packet, it clears that player's completion name, asks vanilla quest selection for a new quest with broadcasting suppressed, sends ordinary Angler packet 74 only to that player, then restores the host's shared quest state.

Do not stack Option A and Option B into the same `Terraria.exe`; restore one before installing the other.

## Why

The Cheat Engine build exists to provide a normal CE executable capable of opening `.CT` tables without using the public bundled-offer installer. The Terraria patchers are separate and do not require Cheat Engine at runtime.

## Provenance

Each Cheat Engine artifact includes `BUILD-PROVENANCE.txt` with the exact upstream Git commit and the SHA-256 hash of the executable produced by that run.

Infinite Angler artifacts include `SHA256.txt` for their self-contained patcher executable.

## Build

Use GitHub Actions:

- **Build clean Cheat Engine 7.5** for the CE artifact.
- **Build Infinite Angler Host** for Option A (host-only).
- **Build Infinite Angler** for Option B (per-PC).

No installer is produced or executed for Cheat Engine itself. The only installers used by CE CI are the official Lazarus/FPC compiler installers referenced by Cheat Engine's own build documentation.
