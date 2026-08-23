# Clean Cheat Engine 7.5 build

This repository does **not** redistribute Cheat Engine's public installer.

Instead, GitHub Actions builds the Windows x64 Cheat Engine 7.5 executable directly from the official source tag:

- Source: `https://github.com/cheat-engine/cheat-engine/tree/7.5`
- Project: `Cheat Engine/cheatengine.lpi`
- Build mode: `Release 64-Bit`
- Toolchain: Lazarus 2.2.2 + FPC 3.2.2, matching upstream's documented build instructions

The workflow uploads a `CheatEngine75-clean-x64` artifact containing the freshly compiled `cheatengine-x86_64.exe` plus the support files from the official 7.5 source tree's `Cheat Engine/bin` directory.

## Why

The goal is a normal Cheat Engine build capable of opening `.CT` tables without using the public bundled-offer installer.

## Provenance

Each artifact includes `BUILD-PROVENANCE.txt` with the exact upstream Git commit and the SHA-256 hash of the executable produced by that run.

## Build

Open **Actions → Build clean Cheat Engine 7.5 → Run workflow**. Builds also run whenever the workflow itself changes.

No installer is produced or executed for Cheat Engine itself. The only installers used by CI are the official Lazarus/FPC compiler installers referenced by Cheat Engine's own build documentation.
