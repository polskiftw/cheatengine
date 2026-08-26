# gloader

Tiny raw-C# runtime mod loader for vanilla Terraria.

gloader loads the installed Terraria executable, compiles each source mod in memory against that exact game build, applies Harmony patches, and then invokes Terraria normally. Terraria's executables are not rewritten on disk and mods are not precompiled DLLs.

## Install layout

Copy the built gloader files directly into the Terraria installation folder. `gloader.exe` sits beside `Terraria.exe`, and its mod folder is `gmods/` beside both of them.

```text
Terraria/
  Terraria.exe
  TerrariaServer.exe
  gloader.exe
  gloader.exe.config
  gmods/
    InfiniteAngler/
    NoLiquidDupe/
    VGMRadio/
    DVDLogo/
```

## gmods folder contract

`gmods/` contains **folders only**. Every immediate subfolder is one mod.

```text
gmods/
  InfiniteAngler/
    Main.cs
  NoLiquidDupe/
    Main.cs
  VGMRadio/
    Main.cs
    NowPlaying.cs
    Providers.cs
    Settings.cs
    VGMRadio.ini
  DVDLogo/
    Main.cs
    DVDLogo.ini
    dvd-logo.png
```

There are no loose mod source, config, asset, or documentation files in the `gmods/` root. gloader only discovers mods from immediate subfolders; loose files are ignored and logged as a warning. The build script also refuses to package a `gmods/` directory containing loose files.

Everything belonging to a mod stays inside that mod's folder: `.cs` source, `.ini`/other configuration, images, data files, and any mod-specific documentation. All `.cs` files beneath one mod folder are compiled together as one in-memory assembly.

Disable one mod by renaming its folder:

```text
Thing/ -> Thing.disabled/
```

A source file inside a mod may also be individually disabled with the `.disabled.cs` suffix, although the normal unit of organization is the whole mod folder.

There is no manifest, custom scripting language, required base class, or gloader-specific inheritance tree. Mods are normal C# and may use Terraria types, reflection, unsafe code, Harmony, and managed dependencies available to the loader.

Each compile receives:

```text
GLOADER
GLOADER_CLIENT   // compiling against Terraria.exe
GLOADER_SERVER   // compiling against TerrariaServer.exe
```

Optional one-time initialization is:

```csharp
public static class Mod
{
    public static void Load()
    {
        // setup
    }
}
```

Harmony attributes are discovered and applied automatically.

## Host & Play server support

Run the visible client through gloader normally from the Terraria folder:

```powershell
.\gloader.exe
```

When Terraria starts `TerrariaServer.exe` for **Multiplayer -> Host & Play**, gloader redirects that child process through another gloader instance using the same `gmods` folder. The original server arguments, working directory, Steam environment, and process relationship are preserved.

This lets server-authoritative mods work for Host & Play without rewriting `TerrariaServer.exe` and without requiring joining players to install anything.

Dedicated server:

```powershell
.\gloader.exe --server
```

Explicit target:

```powershell
.\gloader.exe --target "C:\Games\Terraria\Terraria.exe"
```

Explicit mods folder override:

```powershell
.\gloader.exe --mods "C:\Games\Terraria\gmods"
```

Disable all mods for one run:

```powershell
.\gloader.exe --no-mods
```

Arguments after `--` are passed to Terraria's entry point.

Client and server logs are separate:

```text
logs/gloader-client.log
logs/gloader-server.log
```

## Included mods

### Infinite Angler

`gmods/InfiniteAngler/Main.cs` is a server-authoritative shared endless Angler quest mod. Vanilla's dawn quest rollover is suppressed, so the current quest stays active until every currently connected player has completed it. The server then performs one normal global Angler quest swap for everyone and starts a fresh round. Players who join become part of the current round; players who disconnect stop counting. Joining clients can remain vanilla.

### No Liquid Dupe

`gmods/NoLiquidDupe/Main.cs` is a server-authoritative fix for the regular-bucket water/lava/honey duplication loop. It keeps the liquid volume conserved for partial regular-bucket scoops while leaving full scoops, Bottomless Buckets, pumps, and normal liquid simulation alone. Joining clients can remain vanilla.

### VGM Radio

`gmods/VGMRadio/` is client-only. It keeps one continuous video-game-music radio stream playing independently of Terraria biome/boss music changes, uses Terraria's Music slider, smoothly ducks while paused, and can show now-playing text.

It currently supports two sources:

- `Rainwave` — choose among `All`, `Game`, `OCReMix`, `Covers`, `Chiptunes`, and `Chill`.
- `GTT` (Game That Tune Radio) — one large mixed VGM station. VGMRadio uses GTT's metadata-preserving spoiler MP3 stream so the now-playing overlay continues to work during GTT guessing-game programming.

Its user settings live beside its source:

```ini
# gmods/VGMRadio/VGMRadio.ini
Source=Rainwave
Station=All
ShowNowPlaying=true
```

`Station` applies only to Rainwave and is ignored when `Source=GTT`.

### DVD Logo

`gmods/DVDLogo/` is client-only. It loads `dvd-logo.png` directly at runtime, bounces it around the screen, and changes to a different bright color on each wall hit.

Its size setting lives beside the mod:

```ini
# gmods/DVDLogo/DVDLogo.ini
Width=192
```

`Width` is the rendered width in pixels; height keeps the PNG's aspect ratio. With the current 2:1 logo, the default renders at 192x96.

## Updates

There is no hardcoded Terraria version check. Each launch recompiles source against the exact installed `Terraria.exe` or `TerrariaServer.exe`.

That does not make a patch immune to game updates: if Re-Logic renames or changes a method/field a mod patches, that mod may need a source edit. It does avoid distributing replacement compiled mod DLLs for routine changes.

## Build

Requirements:

- Windows
- .NET SDK capable of building `net48`

From the `gloader` source folder:

```powershell
.\build.ps1
```

Output staging folder:

```text
dist/gloader/
```

Copy the **contents** of `dist/gloader/` directly into the Terraria installation folder. Do not put them inside a nested `gloader` directory.

Raw source mods execute with the same privileges as Terraria. Only use code you trust.
