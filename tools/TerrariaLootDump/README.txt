Terraria Loot Dumper
====================

Purpose
-------
Dump the actual runtime Terraria ItemDropsDB from the copy of Terraria.exe you are currently running.
The dumper is designed for vanilla Terraria 1.4.5.x and uses reflection instead of compile-time Terraria references, so the exact runtime types/fields in your installed build are what get recorded.

What it reads
-------------
- Terraria.Main.ItemDropsDB
- ItemDropDatabase.GetRulesForNPCID(npcId, includeGlobalDrops)
- Runtime IItemDropRule objects and their fields
- Nested/chained loot rules
- NPC and item localized names

What it does NOT do
-------------------
- Does not call NPC.NPCLoot
- Does not call TryDroppingItem
- Does not roll drops
- Does not alter your player/world
- Does not change loot chances
- Does not write into Terraria game state

How to use
----------
1. Keep TerrariaLootDump.CT and TerrariaLootDump.dll in the same folder.
2. Start vanilla Terraria and wait until at least the title screen. Loading your world first is also fine.
3. Start Cheat Engine and attach it to Terraria.exe.
4. Open TerrariaLootDump.CT.
5. Tick: DUMP TERRARIA LOOT DATABASE
6. Wait for the completion message.
7. A new folder named "dump" will appear beside the CT/DLL.
8. Zip that dump folder and send it back to Rin/ChatGPT for analysis.

Output
------
dump/lootdump.json
    Full recursive rule tree. This is the important archival file.

dump/rules.csv
    Flattened one-row-per-rule view for analysis/searching.

dump/npcs.csv
    NPC IDs/names and rule counts.

dump/rule-types.csv
    Every concrete runtime loot-rule class discovered and how often it appears.

dump/MANIFEST.txt
    Detected Terraria/CLR versions and dump counts.

dump/STATUS.txt
    DONE on success.

dump/ERROR.txt
    Written only if the managed dumper throws an exception.

Notes
-----
- The dump includes NPC-specific and global drop rules. The "scope" field marks which is which.
- Nested rules are preserved rather than pretending every drop is a simple item/chance pair.
- The CSV is intentionally lossy/convenient; lootdump.json is the source of truth.
- Re-running overwrites the files in the dump folder with a fresh snapshot.
