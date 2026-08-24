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
  InfiniteAngler/
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

## Disable a mod

- `Thing.cs` -> rename to `Thing.disabled.cs`
- `Thing/` -> rename the folder to `Thing.disabled/`

A compile or load failure disables only that source mod for that run and is written
to `gloader/logs/gloader.log`.
