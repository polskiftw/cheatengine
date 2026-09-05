# Third-Party Notices

This repository contains or packages third-party software in addition to code maintained here.

The licensing terms for Cheat Engine itself remain those of the upstream Cheat Engine project. This file documents additional third-party software introduced by this repository's own build and table-support tooling; it does not replace, modify, or relicense Cheat Engine or any upstream component.

## Harmony

The Terraria x64/CoreCLR Cheat Engine table support runtime includes Harmony as a managed runtime patching dependency.

- Component: Harmony
- NuGet package: `Lib.Harmony`
- Version: `2.4.2`
- Runtime assembly: `Tables/Terraria/runtime/0Harmony.dll`
- Upstream source: https://github.com/pardeike/Harmony
- License: MIT
- Copyright: Copyright (c) 2017 Andreas Pardeike

`Tables/Terraria/Helper/TerrariaCEHelper.csproj` pins the package version used to produce the packaged runtime. The repository does not maintain a fork of Harmony; GitHub Actions restores the pinned package and places its runtime assembly beside `TerrariaCEHelper.dll`.

### MIT License

Copyright (c) 2017 Andreas Pardeike

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
