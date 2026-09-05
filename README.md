# Clean Cheat Engine 7.5 build

This repository does **not** redistribute Cheat Engine's public installer.

Instead, GitHub Actions builds the Windows x64 Cheat Engine 7.5 executable directly from the official source tag:

- Source: `https://github.com/cheat-engine/cheat-engine/tree/7.5`
- Project: `Cheat Engine/cheatengine.lpi`
- Build mode: `Release 64-Bit`
- Toolchain: Lazarus 2.2.2 + FPC 3.2.2, matching upstream's documented build instructions

The workflow uploads a `CheatEngine75-clean-x64` artifact containing the freshly compiled `cheatengine-x86_64.exe` plus the support files from the official 7.5 source tree's `Cheat Engine/bin` directory.

## Why

The repository exists to provide a normal clean Cheat Engine executable capable of opening `.CT` tables without using the public bundled-offer installer.

## Provenance

Each Cheat Engine artifact includes `BUILD-PROVENANCE.txt` with the exact upstream Git commit and the SHA-256 hash of the executable produced by that run.

## Build

Use the **Build clean Cheat Engine 7.5** GitHub Actions workflow.

No installer is produced or executed for Cheat Engine itself. The only installers used by CI are the official Lazarus/FPC compiler installers referenced by Cheat Engine's own build documentation.

## Licensing and third-party software

Cheat Engine itself remains governed by the licensing terms of the upstream Cheat Engine project; this repository does not replace or redefine those terms.

Repository-maintained Terraria helper binaries are reproducible from the checked-in source under `Tables/Terraria/Helper/`. Additional third-party components introduced by this repository are documented in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md), including exact package versions, upstream source locations, and license text.
