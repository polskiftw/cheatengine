$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$Dist = Join-Path $Root "dist\gloader"
$Mods = Join-Path $Root "gmods"

$LooseFiles = @(Get-ChildItem $Mods -File)
if ($LooseFiles.Count -gt 0) {
    $Names = ($LooseFiles | ForEach-Object { $_.Name }) -join ", "
    throw "gmods root must contain only mod subfolders. Move these loose files into their mod folder: $Names"
}

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

dotnet publish $Project -c Release -o $Dist

Copy-Item $Mods (Join-Path $Dist "gmods") -Recurse -Force

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "gloader.exe should sit beside Terraria.exe, with gmods beside them."
