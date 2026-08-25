# DVD Logo

Client-side gloader mod that keeps a classic DVD logo bouncing around the Terraria screen.

Files in this mod folder:

- `Main.cs` — motion, collision, tinting, and config loading.
- `dvd-logo.png` — the normal transparent PNG asset loaded at runtime.
- `DVDLogo.ini` — user-facing settings.

`Width` in `DVDLogo.ini` controls the rendered logo width in pixels. The image keeps its original aspect ratio, so the default `Width=192` renders the current 2:1 logo at 192x96.

The logo starts at a random screen position and angle, reflects from the screen edges, and changes to a visibly different bright color on every bounce. A corner collision counts as one bounce/color change. The server build is a no-op.
