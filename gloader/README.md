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
```

Terraria's executable is not rewritten on disk. Mods are not precompiled DLLs.

Harmony itself is a runtime patching library and does not modify the target files on
disk. The current Lib.Harmony package is MIT licensed.

## Why this survives updates better

There is no hardcoded Terraria version check. Every launch recompiles the mod source
against the `Terraria.exe` that is actually installed.

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
- one broken mod does not stop the remaining mods from compiling/loading;
- no hard version lock;
- per-run log file.

Not included yet:

- hot reload/unload while Terraria is already running;
- dependency/load-order manifests;
- mod sandboxing/security;
- content registration (new items/NPCs/tiles/etc.);
- a GUI.

Raw source mods execute with the same privileges as Terraria. Only use code you trust.
