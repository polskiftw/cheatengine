# DVD Logo

Client-side gloader mod that keeps a classic DVD logo bouncing around the Terraria screen.

The mod is intentionally simple:

- `Main.cs` is compiled by gloader at runtime;
- `dvd-logo.png` sits beside it as a normal PNG and is loaded directly at runtime;
- the logo moves continuously and reflects off the current screen edges;
- every wall bounce picks a visibly different bright hue;
- a corner hit changes color once;
- window/resolution changes use the current screen bounds;
- the dedicated-server build is a no-op.

The PNG is not embedded, converted, or encoded into the C# source.
