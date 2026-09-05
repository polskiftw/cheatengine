# Terraria CE runtime

This directory contains generated runtime assemblies used by the x64/CoreCLR Terraria Cheat Engine table.

- `TerrariaCEHelper.dll` is built from the first-party source in `../Helper/`.
- `0Harmony.dll` is the pinned third-party Harmony dependency restored from `Lib.Harmony` version `2.4.2` during the GitHub Actions build.

Do not edit these DLLs by hand. Change the source/project files and let CI rebuild the packaged runtime.

Third-party licensing and source provenance are documented in [`../../../THIRD-PARTY-NOTICES.md`](../../../THIRD-PARTY-NOTICES.md).
