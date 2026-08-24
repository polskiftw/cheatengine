# Clean Cheat Engine 7.5 build

This repository does **not** redistribute Cheat Engine's public installer.

Instead, GitHub Actions builds the Windows x64 Cheat Engine 7.5 executable directly from the official source tag:

- Source: `https://github.com/cheat-engine/cheat-engine/tree/7.5`
- Project: `Cheat Engine/cheatengine.lpi`
- Build mode: `Release 64-Bit`
- Toolchain: Lazarus 2.2.2 + FPC 3.2.2, matching upstream's documented build instructions

The workflow uploads a `CheatEngine75-clean-x64` artifact containing the freshly compiled `cheatengine-x86_64.exe` plus the support files from the official 7.5 source tree's `Cheat Engine/bin` directory.

## Terraria: Infinite Angler

This repo also contains `terraria/infinite-angler`, an old-school drop-in patcher for vanilla Terraria. It removes the Angler's once-per-day player cooldown and rolls another quest after each successful reward while leaving normal fishing, reward generation, lifetime quest progression, and Host & Play intact.

The initial reference target is Terraria **1.4.5.8**, but the patcher is deliberately **not hard-locked to a version string**. It validates the live Angler IL structure and refuses unfamiliar code instead of guessing.

The `Build Infinite Angler` workflow publishes a self-contained Windows x64 `InfiniteAngler.exe`. Put it beside `Terraria.exe`, run it once on each PC that should have endless personal Angler quests, then launch Terraria normally through Steam.

See `terraria/infinite-angler/README.md` and `terraria/infinite-angler/DESIGN.md` for install, restore, and implementation details.

## Why

The Cheat Engine build exists to provide a normal CE executable capable of opening `.CT` tables without using the public bundled-offer installer. The Terraria patcher is separate and does not require Cheat Engine at runtime.

## Provenance

Each Cheat Engine artifact includes `BUILD-PROVENANCE.txt` with the exact upstream Git commit and the SHA-256 hash of the executable produced by that run.

Infinite Angler build artifacts include `SHA256.txt` for the self-contained patcher executable.

## Build

Use GitHub Actions:

- **Build clean Cheat Engine 7.5** for the CE artifact.
- **Build Infinite Angler** for the Terraria drop-in patcher.

No installer is produced or executed for Cheat Engine itself. The only installers used by CE CI are the official Lazarus/FPC compiler installers referenced by Cheat Engine's own build documentation.
