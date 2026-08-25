# DVD Logo

Client-side gloader mod that keeps a classic DVD logo bouncing around the Terraria screen.

- uses `dvd-logo.png` as the logo asset;
- movement is based on real frame time, so speed stays consistent across frame rates;
- the logo changes to a different bright color on every wall bounce;
- hitting a corner changes color once, not twice;
- window/resolution changes are handled by the current screen bounds;
- the server build is a no-op.

The mod is included automatically when gloader's `build.ps1` copies the `Mods` folder into the distribution.

Disable it by renaming `DVDLogo` to `DVDLogo.disabled`.
