# Terraria Cheat Engine tables

This directory contains Terraria `.CT` tables maintained for the clean Cheat Engine build in this repository.

## Current x64 work

`Terraria-1.4.5.x-Table-Ver-7-CE45-x64-CoreCLR-Experimental.CT` is the experimental 64-bit/CoreCLR port targeting Claire's x64 Terraria 1.4.5.8 environment. It is intentionally marked experimental until the live CoreCLR paths are proven.

The CI smoke test under `tests/coreclr-injection` independently verifies whether Cheat Engine 7.5 can inject and execute a managed helper assembly inside an x64 .NET/CoreCLR target. That test does not use gloader mods or Terraria mods.
