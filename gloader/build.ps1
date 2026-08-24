$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$Dist = Join-Path $Root "dist\gloader"

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

dotnet publish $Project -c Release -o $Dist

Copy-Item (Join-Path $Root "Mods") (Join-Path $Dist "Mods") -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Put that gloader folder inside the Terraria installation folder."
