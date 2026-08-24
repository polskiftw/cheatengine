# Infinite Angler patch design

The patch is intentionally narrow and client-local.

## Vanilla behavior used

- The Angler turn-in path calls `Player.GetAnglerReward(...)` only after a requested fish has actually been accepted.
- Terraria tracks daily completion with `Main.anglerQuestFinished` plus `Main.anglerWhoFinishedToday`.
- Multiplayer message 75 records a player's name as finished on the server; it does not grant the reward or select a new quest.
- `Main.AnglerQuestSwap()` already contains vanilla quest-selection restrictions, but vanilla deliberately returns early from it on a multiplayer client.

## Patch behavior

1. At entry to the uniquely identified Angler turn-in routine, reset the local completion flag and clear the local completion-name cache.
2. Leave fish removal, reward calculation and `anglerQuestsFinished` incrementing untouched.
3. Immediately after a successful `GetAnglerReward(...)`, invoke a tiny injected helper.
4. If this process is a multiplayer client (`netMode == 1`), the helper temporarily uses local single-player mode only for the synchronous call to `AnglerQuestSwap()`, then restores `netMode`.
5. Therefore the next quest is rolled using Terraria's own selection code without rolling the host/server's quest or waiting for another player.

## Compatibility policy

There is no exact Terraria-version allowlist. Before writing, the patcher requires the relevant types, fields, quest-swap method, reward method, and one uniquely identifiable turn-in routine to match structurally. If an update changes that structure, the patcher refuses the file instead of guessing.

The initial target/reference release is Terraria 1.4.5.8.
