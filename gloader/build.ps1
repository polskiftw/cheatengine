$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$Dist = Join-Path $Root "dist\gloader"
$Mods = Join-Path $Root "Mods"

$LooseFiles = @(Get-ChildItem $Mods -File)
if ($LooseFiles.Count -gt 0) {
    $Names = ($LooseFiles | ForEach-Object { $_.Name }) -join ", "
    throw "Mods root must contain only mod subfolders. Move these loose files into their mod folder: $Names"
}

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

dotnet publish $Project -c Release -o $Dist

Copy-Item $Mods (Join-Path $Dist "Mods") -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Put that gloader folder inside the Terraria installation folder."
