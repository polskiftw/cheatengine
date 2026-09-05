# Terraria Cheat Engine tables

This directory contains Terraria `.CT` tables maintained for the clean Cheat Engine build in this repository.

## Current x64 work

`Terraria-1.4.5.x-Table-Ver-7-CE45-x64-CoreCLR-Experimental.CT` is the experimental 64-bit/CoreCLR port targeting Claire's x64 Terraria 1.4.5.8 environment.

### All Fish Are Crates

`🎣 All Fish Are Crates (vanilla rarity rolls preserved)` is implemented in the x64 table.

The table uses Cheat Engine 7.5's own CoreCLR `injectDotNetDLL` path to load `runtime/TerrariaCEHelper.dll` into the running `gloader.exe` process. The helper applies a Harmony postfix to `Terraria.Projectile.FishingCheck_RollDropLevels` and changes only the method's final `ref/out bool` to `true` after vanilla has completed its other fishing rarity/quality rolls.

Keep the `runtime` directory beside the `.CT`; it contains `TerrariaCEHelper.dll` and its `0Harmony.dll` dependency. Helper source is in `Helper/`. This is a Cheat Engine table feature and does not use gloader mods or Terraria mods.

## CoreCLR injection test

The CI smoke test under `tests/coreclr-injection` independently verifies that Cheat Engine 7.5 can inject and execute a managed helper assembly inside an x64 .NET/CoreCLR target.
