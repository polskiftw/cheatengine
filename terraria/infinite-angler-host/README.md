# Infinite Angler Host (Option A)

Host-only endless Angler quests for vanilla Terraria **Host & Play**.

Only the host installs this patch. Joining players use completely unmodified Terraria.

## Behavior

After any connected player successfully completes an Angler quest:

1. Terraria records the normal completion packet.
2. The host immediately removes only that player's daily completion lock.
3. The host asks vanilla `Main.AnglerQuestSwap()` for a valid next quest while networking is temporarily suppressed.
4. The host sends vanilla Angler Quest packet **74** only to that player, with `completed = false`.
5. The host restores the world's shared quest immediately.

No custom network packets are introduced. Vanilla guests only receive data their normal client already understands.

Players are independent: one player can do twenty quests while another does none. A reroll for one guest is not broadcast to anyone else.

## Install

1. Close Terraria.
2. Put `InfiniteAnglerHost.exe` beside `Terraria.exe`.
3. Run `InfiniteAnglerHost.exe` once.
4. Launch Terraria normally through Steam.
5. Use **Multiplayer -> Host & Play** as usual.

Friends/siblings joining the host do **not** install anything.

## Compatibility and safety

The patcher is not hard-locked to `1.4.5.8` or any other version string. It validates the structures it actually modifies:

- `Terraria.MessageBuffer`'s server-side Angler completion-name path
- `Main.anglerWhoFinishedToday`
- `Main.anglerQuest`
- `Main.AnglerQuestSwap()`
- the vanilla packet-74 serializer in `NetMessage.SendData`
- `NetworkText.FromLiteral(string)`

If those structures stop matching after an update, the patcher refuses before replacing `Terraria.exe`.

The original executable is backed up under `InfiniteAnglerHost-backups/`, and the install records SHA-256 hashes in `InfiniteAnglerHost.manifest.json`.

Restore with:

```powershell
.\InfiniteAnglerHost.exe --restore
```

Compatibility-only check:

```powershell
.\InfiniteAnglerHost.exe --check
```

## Option A vs Option B

- **Option A / InfiniteAnglerHost**: only the Host & Play PC is patched; vanilla guests get endless quests while connected to that host.
- **Option B / InfiniteAngler**: each PC patches itself and keeps endless personal quests regardless of who hosts.

Do not stack both patches into the same `Terraria.exe`. Restore one before installing the other.

## Verification

CI builds a synthetic Terraria host plus a deliberately unmodified guest consumer. It then:

- structurally locates the completion path,
- installs the host patch,
- simulates a vanilla client completion,
- verifies only that player receives packet 74,
- verifies `completed = false`,
- verifies the next quest was rerolled,
- verifies the host's global quest and `netMode` are restored,
- lets an unpatched guest consume the packet and confirms it can quest again,
- restores the fixture byte-for-byte.
