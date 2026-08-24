# Mods

Drop raw C# source here.

## One-file mod

```text
Mods/
  InfiniteAngler.cs
```

That file is compiled in memory every time gloader starts.

## Multi-file mod

Put the files in one immediate subfolder:

```text
Mods/
  Thing/
    Main.cs
    Patches.cs
    Helpers.cs
```

The whole folder becomes one compiled mod assembly.

## What a mod can use

A mod is normal C#. gloader compiles it against:

- the exact `Terraria.exe` or `TerrariaServer.exe` being launched;
- managed DLLs beside Terraria;
- Harmony (`HarmonyLib`);
- the normal .NET Framework assemblies already loaded.

There is no required gloader API, manifest, base class, or precompiled mod DLL.

Classes decorated with Harmony patch attributes are patched automatically. Do **not**
call `PatchAll()` yourself unless you intentionally want to manage your own Harmony
instance.

If a mod needs one-time startup code, it may optionally contain exactly one class
named `Mod` with a static parameterless `Load()` method:

```csharp
public static class Mod
{
    public static void Load()
    {
        // optional one-time initialization
    }
}
```

`Mod.Load()` is optional. A Harmony-only mod can contain only patch classes.

## Client and server code

gloader defines these C# preprocessor symbols automatically:

```text
GLOADER
GLOADER_CLIENT   // compiling against Terraria.exe
GLOADER_SERVER   // compiling against TerrariaServer.exe
```

So a single raw source mod can do this:

```csharp
#if GLOADER_SERVER
// server-authoritative patches
#else
// client-side patches, or a no-op
#endif
```

Normal Host & Play automatically starts the child `TerrariaServer.exe` through
another gloader instance using the **same Mods folder**. You do not need a second
server install step or a compiled server plugin.

The included `InfiniteAngler.cs` uses this model: it is inert in the visible client
and active in the Host & Play/dedicated server process. Joining players stay vanilla.

## Disable a mod

- `Thing.cs` -> rename to `Thing.disabled.cs`
- `Thing/` -> rename the folder to `Thing.disabled/`

A compile or load failure disables only that source mod for that run. Client and
server errors are written separately to:

```text
gloader/logs/gloader-client.log
gloader/logs/gloader-server.log
```
