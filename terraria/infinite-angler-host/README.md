# Infinite Angler Host (Option A)

Host-only endless Angler quests for vanilla Terraria **Host & Play**.

Only the host PC installs this patch. Joining players use completely unmodified Terraria.

## What gets patched

Vanilla Host & Play launches **`TerrariaServer.exe`** as the multiplayer authority. Option A therefore patches `TerrariaServer.exe`, not the visible `Terraria.exe` client.

Put `InfiniteAnglerHost.exe` in the normal Terraria install folder containing both executables. With no command-line arguments it deliberately selects `TerrariaServer.exe`.

## Behavior

After any connected player successfully completes an Angler quest:

1. Vanilla `TerrariaServer.exe` records the normal completion packet.
2. The patch immediately removes only that completing player's name from `Main.anglerWhoFinishedToday`.
3. It snapshots the shared `Main.anglerQuest`, `Main.anglerQuestFinished`, and `Main.netMode` values.
4. It temporarily suppresses networking and asks vanilla `Main.AnglerQuestSwap()` to choose another valid quest.
5. It restores server mode and sends the normal vanilla Angler Quest message only to the completing player, with that player's completion state now false.
6. It restores the shared world quest and `anglerQuestFinished` values.

No custom network packets are introduced. Vanilla clients only receive a standard Angler quest-state message their normal game already understands.

Players can therefore progress independently during the hosted session. One player can repeatedly finish quests without forcing the others to install anything.

## Install

1. Close Terraria.
2. Put `InfiniteAnglerHost.exe` in the Terraria install folder beside **`TerrariaServer.exe`**.
3. Run `InfiniteAnglerHost.exe` once.
4. It should print a target ending in `TerrariaServer.exe` and create a matching backup.
5. Launch `Terraria.exe` normally through Steam.
6. Use **Multiplayer -> Host & Play** as usual.

Friends/siblings joining the host do **not** install anything.

## Compatibility and safety

The initial reference target is Terraria **1.4.5.8**, but the patcher is not hard-locked to a version string or to packet number 74.

Before writing anything it validates:

- `Terraria.MessageBuffer`'s server-side Angler completion-name path,
- `Main.anglerWhoFinishedToday`,
- `Main.anglerQuest` and `Main.anglerQuestFinished`,
- `Main.AnglerQuestSwap()`, including a guard that rejects it if it starts directly touching the per-name completion list or writing additional shared `Main` state,
- `Terraria.ID.MessageID.AnglerQuest`, `AnglerQuestFinished`, and `QuestsCountSync` as live constants from the target assembly,
- the vanilla Angler serialization dependencies inside `NetMessage.SendData`,
- `NetworkText.FromLiteral(string)`.

If those structures stop matching after an update, the patcher refuses before replacing the server executable.

The original server executable is backed up under `InfiniteAnglerHost-backups/`, with original and patched SHA-256 hashes stored in `InfiniteAnglerHost.manifest.json`.

Restore with:

```powershell
.\InfiniteAnglerHost.exe --restore
```

Compatibility-only check:

```powershell
.\InfiniteAnglerHost.exe --check
```

An explicit target remains available for testing or unusual layouts:

```powershell
.\InfiniteAnglerHost.exe --target "C:\path\to\TerrariaServer.exe"
```

## Option A vs Option B

- **Option A / InfiniteAnglerHost** patches the host's `TerrariaServer.exe`. Everyone connected to that Host & Play server can receive endless quests while their clients remain vanilla.
- **Option B / InfiniteAngler** patches a player's own `Terraria.exe`, so that individual gets the local behavior regardless of who hosts.

Because the corrected versions normally target different executables, they are conceptually separate rather than two patches stacked into one file.

## Verification

CI intentionally recreates the file-layout mistake that the first Option A build missed. It places both a synthetic `Terraria.exe` and `TerrariaServer.exe` beside the patcher and invokes the patcher **without `--target`**. The test then verifies:

- `TerrariaServer.exe` is modified,
- `Terraria.exe` remains byte-for-byte unchanged,
- the Angler packet ID is discovered from `MessageID` rather than hardcoded (the fixture deliberately uses 174 instead of 74),
- two different vanilla guests can complete quests,
- one guest can complete repeatedly,
- packets are targeted only to the completing guest,
- the packet reports `completed = false`,
- another player's existing completion-list entry survives,
- the server's shared quest, `anglerQuestFinished`, and `netMode` are restored,
- an unmodified guest consumer accepts the new quest state,
- restore reproduces the original server assembly byte-for-byte.

This is still not a substitute for testing against the exact retail 1.4.5.8 `TerrariaServer.exe`; that is the next audit layer.
