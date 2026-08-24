# Infinite Angler

A tiny old-school drop-in patcher for vanilla Terraria. It removes the Angler's once-per-day player lockout and rolls another quest immediately after a successful turn-in.

Designed for current vanilla Terraria **1.4.5.8** and, intentionally, **not hard-locked to that version number**. The patcher validates the Angler code structure before modifying anything. A later point release is accepted if the relevant IL still matches; an incompatible change is refused.

## What it changes

- Normal quest fish and fishing rules stay intact.
- Normal Angler reward code stays intact.
- `anglerQuestsFinished` still increments normally.
- After a successful reward, Terraria's own `AnglerQuestSwap()` logic rolls the next quest locally.
- The local per-day completion cache is ignored/reset, so there is no waiting for 4:30 AM.
- Multiplayer clients do not wait for each other. Each patched client can continue its own quest chain.

## Install

1. Close Terraria.
2. Copy `InfiniteAngler.exe` into the normal Terraria install folder, beside `Terraria.exe`.
3. Run `InfiniteAngler.exe` once.
4. Launch Terraria normally through Steam.
5. For Host & Play, run the same patcher once on each PC that should have endless Angler quests.

The patcher makes a hash-named original backup under `InfiniteAngler-backups/` and writes `InfiniteAngler.manifest.json` beside Terraria.

## Restore

From a terminal in the Terraria folder:

```powershell
.\InfiniteAngler.exe --restore
```

Restore is hash-guarded. If Steam has replaced/updated `Terraria.exe`, the patcher refuses to copy an older backup over the newer game.

## Compatibility check only

```powershell
.\InfiniteAngler.exe --check
```

You can also point it at a different install explicitly:

```powershell
.\InfiniteAngler.exe --check --target "D:\SteamLibrary\steamapps\common\Terraria\Terraria.exe"
```

## Safety model

This does not use an exact `1.4.5.8` string as a gate. Before writing, it requires the live assembly to contain the expected Terraria types/fields, `Main.AnglerQuestSwap()`, `Player.GetAnglerReward(...)`, and one uniquely identifiable turn-in method that references the Angler completion state. If that shape changes, it exits without replacing `Terraria.exe`.

A patch marker prevents accidental double-patching.
