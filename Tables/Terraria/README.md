# Terraria Cheat Engine tables

This directory contains Terraria `.CT` tables maintained for the clean Cheat Engine build in this repository.

## Current x64 work

`Terraria-1.4.5.x-Table-Ver-7-CE45-x64-CoreCLR-Experimental.CT` is the experimental 64-bit/CoreCLR port targeting Claire's x64 Terraria 1.4.5.8 environment.

### All Fish Are Crates

`🎣 All Fish Are Crates (vanilla rarity rolls preserved)` is implemented in the x64 table.

The table uses Cheat Engine 7.5's own CoreCLR `injectDotNetDLL` path to load `runtime/TerrariaCEHelper.dll` into the running `gloader.exe` process. The helper applies a Harmony postfix to `Terraria.Projectile.FishingCheck_RollDropLevels` and changes only the method's final `ref/out bool` to `true` after vanilla has completed its other fishing rarity/quality rolls.

### Lucky Treasure Bags

`🎁 Lucky Treasure Bags (all chance drops succeed, RNG preserved)` is implemented in the x64 table.

Vanilla `Player.OpenBossBag` runs normally. The CE-injected helper observes the real `Player.QuickSpawnItem(IEntitySource,int,int)` calls and, after vanilla finishes, supplements only independent chance-based drops that did not occur. Random-choice groups stay random, guaranteed drops stay vanilla, and fixed/random stack behavior is not rerolled. The 1.4.5.8 chance map was rebuilt from the clean decompile and includes item `5483` in bag `3322`, an independent 1-in-9 roll omitted by the older x86 table.

For the Hardmode boss bags whose vanilla branches call `TryGettingDevArmor`, the helper preserves a naturally rolled developer set. If vanilla misses, it retries the real vanilla routine until exactly one random developer set succeeds (maximum 512 attempts). Queen Slime bag `4957` remains excluded because vanilla does not call `TryGettingDevArmor` for it.

Keep the `runtime` directory beside the `.CT`; it contains `TerrariaCEHelper.dll` and its `0Harmony.dll` dependency. Helper source is in `Helper/`. These are Cheat Engine table features and do not use gloader mods or Terraria mods. The original 32-bit table is not modified by this x64 work.

## CoreCLR injection test

The CI smoke test under `tests/coreclr-injection` independently verifies that Cheat Engine 7.5 can inject and execute a managed helper assembly inside an x64 .NET/CoreCLR target.
