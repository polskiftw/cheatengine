# gloader

Tiny raw-C# runtime mod loader for vanilla Terraria.

This is intentionally **not** a tModLoader replacement. The goal is the exact small
patch workflow we want: put C# source in a folder, start Terraria, and let the loader
compile that source against the installed game at runtime.

## Design

```text
gloader.exe
   |
   +-- loads the installed Terraria.exe as a managed assembly
   +-- scans gloader/Mods/
   +-- Roslyn compiles each source mod in memory
   +-- Harmony applies the mod's runtime patches
   +-- invokes Terraria's normal entry point
   +-- Host & Play TerrariaServer.exe launches are routed through gloader too
```

Terraria's executables are not rewritten on disk. Mods are not precompiled DLLs.

Harmony itself is a runtime patching library and does not modify the target files on
disk. The current Lib.Harmony package is MIT licensed.

## Host & Play server support

Run the visible client through gloader normally:

```powershell
.\gloader\gloader.exe
```

When vanilla Terraria later starts `TerrariaServer.exe` for **Multiplayer -> Host &
Play**, gloader intercepts that exact child-process launch and transparently changes
it to:

```text
gloader.exe --server --target TerrariaServer.exe --mods <same Mods folder> -- <vanilla server arguments>
```

The original Host & Play arguments, working directory, Steam environment, and process
relationship are preserved. The gloader process becomes the server process and hosts
`TerrariaServer.exe` in-process, so Terraria's client still owns and observes the
same child process it launched.

This means server-authoritative raw mods in `Mods/` work in normal Host & Play without
patching `TerrariaServer.exe` on disk and without requiring joining players to install
anything.

`--no-mods` also disables the Host & Play redirect for that run, so the child server
starts completely vanilla.

Client and server write separate logs:

```text
gloader/logs/gloader-client.log
gloader/logs/gloader-server.log
```

## Why this survives updates better

There is no hardcoded Terraria version check. Every launch recompiles the mod source
against the exact `Terraria.exe` or `TerrariaServer.exe` that is actually installed.

That does **not** make a mod magically compatible when Re-Logic changes the method
or field the mod patches. It does mean the fix is usually just editing the `.cs`
source and restarting; the mod does not need a Visual Studio project or a distributed
replacement DLL.

gloader itself should only need changes when Terraria changes something fundamental
about how the game can be hosted or patched.

## Folder layout after building

Put the built folder inside the Terraria installation:

```text
Terraria/
  Terraria.exe
  TerrariaServer.exe
  ...
  gloader/
    gloader.exe
    Mods/
      InfiniteAngler.cs
      NoLiquidDupe.cs
    logs/
```

Then run:

```powershell
.\gloader\gloader.exe
```

Because it is one folder below Terraria, it will find `..\Terraria.exe`
automatically.

Dedicated server:

```powershell
.\gloader\gloader.exe --server
```

Explicit target:

```powershell
.\gloader\gloader.exe --target "C:\Games\Terraria\Terraria.exe"
```

Disable all mods for a run:

```powershell
.\gloader\gloader.exe --no-mods
```

Arguments after `--` are passed to Terraria's entry point:

```powershell
.\gloader\gloader.exe -- -someGameArgument
```

## Source mod rules

See [`Mods/README.md`](Mods/README.md).

The short version:

```text
Mods/Thing.cs
```

or:

```text
Mods/Thing/
  Main.cs
  MorePatches.cs
```

No manifest. No custom scripting language. No required gloader inheritance tree.

A mod can use normal C#, Terraria types, reflection, unsafe code, and Harmony directly.
Harmony attributes are applied automatically.

Each compile also gets simple target symbols:

```text
GLOADER
GLOADER_CLIENT   // Terraria.exe
GLOADER_SERVER   // TerrariaServer.exe
```

That lets one raw source file contain client-only and server-only code without adding
a gloader API or metadata format.

Optional one-time initialization is just:

```csharp
public static class Mod
{
    public static void Load()
    {
        // setup
    }
}
```

## Included mod: Infinite Angler

`Mods/InfiniteAngler.cs` is the server-authoritative endless Angler quest mod.

On the visible client it compiles to a no-op. In a Host & Play or dedicated server
process it watches vanilla's Angler completion packet. It records whether the player
was already in `Main.anglerWhoFinishedToday` before vanilla handles the packet, then
only acts if vanilla successfully adds that player's name.

After a successful completion it:

1. removes only that completing player's daily completion entry;
2. snapshots the shared Angler quest state;
3. temporarily suppresses networking and asks vanilla `Main.AnglerQuestSwap()` to
   choose another valid quest;
4. sends the normal vanilla Angler Quest packet only to the completing player, with
   that player's completion state false;
5. restores the server's shared Angler quest state.

No custom packet format is introduced. Joining clients can remain completely vanilla.

## Included mod: No Liquid Dupe

`Mods/NoLiquidDupe.cs` is a server-authoritative fix for vanilla regular-bucket liquid
duplication.

Vanilla can turn an Empty Bucket into a completely full bucket after collecting only
100-254 of the 255 liquid units represented by a full tile. That makes the familiar
small U-shaped infinite-water/lava/honey loop possible.

The mod leaves Terraria's liquid packet parser intact. Around an incoming liquid
update from a player holding a **regular** Empty/Water/Lava/Honey Bucket, it snapshots
a bounded area around that player before vanilla handles the packet and compares the
liquid volume afterward.

- a full 255-unit scoop is unchanged;
- if a scoop removes only 100-254 units, the server records only the artificial
  `255 - removed` excess for that player and liquid type;
- when that player later pours that liquid, the server removes exactly the outstanding
  artificial excess from only the liquid that was newly added by that placement;
- corrected tiles are synchronized using Terraria's own `NetMessage.sendWater`
  serializer, so no custom client packet or client mod is needed;
- water, lava, and honey are covered;
- Bottomless Buckets are not regular bucket item IDs and are deliberately untouched;
- pumps and ordinary liquid simulation do not run through the regular-bucket filter.

The conservation ledger is kept in memory by player name for the current server
process. It is intentionally a lightweight gameplay fix rather than an adversarial
anti-cheat system.

As with Infinite Angler, the visible Host & Play client compiles this mod to a no-op.
The redirected `TerrariaServer.exe` applies it for everyone, including completely
vanilla joining clients.

## Build

Requirements:

- Windows
- .NET SDK capable of building `net48`

From this folder:

```powershell
.\build.ps1
```

Output:

```text
dist/gloader/
```

The project targets .NET Framework 4.8 because current lightweight vanilla Terraria
1.4.5 patching projects are operating in the same classic managed-runtime family,
and current Terraria injector projects successfully use the same-process
"load Terraria assembly, patch, invoke entry point" model.

## v0.1 scope

Included now:

- raw `.cs` mods;
- one-file and multi-file mods;
- in-memory Roslyn compilation;
- direct references to the live installed Terraria assemblies;
- automatic Harmony patch discovery;
- optional `Mod.Load()`;
- client (`Terraria.exe`) and dedicated-server (`TerrariaServer.exe`) targets;
- automatic Host & Play server routing through gloader;
- shared Mods folder between Host & Play client/server;
- client/server preprocessor symbols;
- separate client/server logs;
- one broken mod does not stop the remaining mods from compiling/loading;
- no hard version lock.

Not included yet:

- hot reload/unload while Terraria is already running;
- dependency/load-order manifests;
- mod sandboxing/security;
- content registration (new items/NPCs/tiles/etc.);
- a GUI.

Raw source mods execute with the same privileges as Terraria. Only use code you trust.
