# DVD Logo

Client-side gloader mod that keeps a classic DVD logo bouncing around the Terraria screen.

- the logo is a built-in 64×32 monochrome mask, so there is no external image file to load or package;
- the mask is tinted at draw time, following the same basic rendering idea used by SM64: Rogue Chaos Edition's DVD effect;
- movement is based on real frame time, so speed stays consistent across frame rates;
- the logo changes to a different bright color on every wall bounce;
- hitting a corner changes color once;
- window/resolution changes are handled by the current screen bounds;
- the server build is a no-op.

The mod is included automatically when gloader's `build.ps1` copies the `Mods` folder into the distribution.

Disable it by renaming `DVDLogo` to `DVDLogo.disabled`.
