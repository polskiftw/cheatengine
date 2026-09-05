$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$ctPath = Join-Path $repo 'Tables/Terraria/Terraria-1.4.5.x-Table-Ver-7-CE45-x64-CoreCLR-Experimental.CT'
if (!(Test-Path $ctPath)) { throw "Terraria x64 CT not found: $ctPath" }

$text = Get-Content -LiteralPath $ctPath -Raw

$oldFishEntry = @'
            <CheatEntry>
              <ID>10836</ID>
              <Description>"⚠ x64 TODO - 🎣 All Fish Are Crates"</Description>
              <Color>40FF00</Color>
              <VariableType>Auto Assembler Script</VariableType>
              <AssemblerScript>[ENABLE]
{$lua}
if syntaxcheck then return end
x64ctUnsupported('All Fish Are Crates','the old generated hook relies on the x86 managed ABI for an out-parameter; x64 argument placement must be re-derived at runtime')
{$asm}

[DISABLE]
{$lua}
if syntaxcheck then return end
{$asm}
</AssemblerScript>
            </CheatEntry>
'@

$newFishEntry = @'
            <CheatEntry>
              <ID>10836</ID>
              <Description>"🎣 All Fish Are Crates (vanilla rarity rolls preserved)"</Description>
              <Color>40FF00</Color>
              <VariableType>Auto Assembler Script</VariableType>
              <AssemblerScript>[ENABLE]
{$lua}
if syntaxcheck then return end
x64ctEnableAllFishAreCrates()
{$asm}

[DISABLE]
{$lua}
if syntaxcheck then return end
x64ctDisableAllFishAreCrates()
{$asm}
</AssemblerScript>
            </CheatEntry>
'@

if ($text.Contains($oldFishEntry)) {
    $text = $text.Replace($oldFishEntry, $newFishEntry)
} elseif (!$text.Contains('x64ctEnableAllFishAreCrates()')) {
    throw 'Could not locate the All Fish Are Crates entry.'
}

$oldLuckyEntry = @'
            <CheatEntry>
              <ID>10838</ID>
              <Description>"⚠ x64 TODO - 🎁 Lucky Treasure Bags (all chance drops succeed, RNG preserved)"</Description>
              <Color>40FF00</Color>
              <VariableType>Auto Assembler Script</VariableType>
              <AssemblerScript>[ENABLE]
{$lua}
if syntaxcheck then return end
x64ctUnsupported('Lucky Treasure Bags','complex generated hooks depend on x86 managed calling convention and stack arguments; needs a dedicated x64 rewrite')
{$asm}

[DISABLE]
{$lua}
if syntaxcheck then return end
{$asm}
</AssemblerScript>
            </CheatEntry>
'@

$newLuckyEntry = @'
            <CheatEntry>
              <ID>10838</ID>
              <Description>"🎁 Lucky Treasure Bags (all chance drops succeed, RNG preserved)"</Description>
              <Color>40FF00</Color>
              <VariableType>Auto Assembler Script</VariableType>
              <AssemblerScript>[ENABLE]
{$lua}
if syntaxcheck then return end
x64ctEnableLuckyTreasureBags()
{$asm}

[DISABLE]
{$lua}
if syntaxcheck then return end
x64ctDisableLuckyTreasureBags()
{$asm}
</AssemblerScript>
            </CheatEntry>
'@

if ($text.Contains($oldLuckyEntry)) {
    $text = $text.Replace($oldLuckyEntry, $newLuckyEntry)
} elseif (!$text.Contains('x64ctEnableLuckyTreasureBags()')) {
    throw 'Could not locate the Lucky Treasure Bags TODO entry.'
}

$stateMarker = 'x64ct.fishCratesEnabled = x64ct.fishCratesEnabled or false'
if (!$text.Contains('x64ct.luckyBagsEnabled = x64ct.luckyBagsEnabled or false')) {
    if (!$text.Contains($stateMarker)) { throw 'Could not locate managed feature state marker.' }
    $text = $text.Replace($stateMarker, $stateMarker + "`r`nx64ct.luckyBagsEnabled = x64ct.luckyBagsEnabled or false")
}

$cleanupMarker = '  pcall(x64ctDisableAllFishAreCrates,true)'
if (!$text.Contains('pcall(x64ctDisableLuckyTreasureBags,true)')) {
    if (!$text.Contains($cleanupMarker)) { throw 'Could not locate managed feature cleanup marker.' }
    $text = $text.Replace($cleanupMarker, $cleanupMarker + "`r`n  pcall(x64ctDisableLuckyTreasureBags,true)")
}

$luaMarker = @'
-- --------------------------------------------------------------------------
-- Buffs and debuff cleaners
-- --------------------------------------------------------------------------
'@

$luckyLua = @'
-- --------------------------------------------------------------------------
-- Lucky Treasure Bags. Vanilla OpenBossBag runs first. The CE-injected helper
-- observes QuickSpawnItem calls, then supplements only independent chance-based
-- drops that did not occur. Random-choice groups and vanilla stack/prefix RNG
-- are left alone. Eligible Hardmode bags get exactly one random developer set.
-- --------------------------------------------------------------------------
function x64ctEnableLuckyTreasureBags()
  x64ctHelperCommand('lucky-on')
  x64ct.luckyBagsEnabled=true
  print('[Terraria x64 CT] Lucky Treasure Bags enabled.')
end

function x64ctDisableLuckyTreasureBags(silent)
  if not x64ct.luckyBagsEnabled then return end
  local ok,err=pcall(x64ctHelperCommand,'lucky-off')
  x64ct.luckyBagsEnabled=false
  if not ok and not silent then error(err) end
  if ok then print('[Terraria x64 CT] Lucky Treasure Bags disabled.') end
end

'@

if (!$text.Contains('function x64ctEnableLuckyTreasureBags()')) {
    if (!$text.Contains($luaMarker)) { throw 'Could not locate Lua feature insertion marker.' }
    $text = $text.Replace($luaMarker, $luckyLua + $luaMarker)
}

$fishComment = 'All Fish Are Crates uses the CE-injected managed helper in Tables/Terraria/runtime; it is not a gmod.'
$luckyComment = ' Lucky Treasure Bags uses the same CE-injected helper and preserves vanilla random-choice groups while guaranteeing independent chance drops.'
if ($text.Contains($fishComment) -and !$text.Contains('Lucky Treasure Bags uses the same CE-injected helper')) {
    $text = $text.Replace($fishComment, $fishComment + $luckyComment)
}

Set-Content -LiteralPath $ctPath -Value $text -Encoding utf8NoBOM

[xml]$xml = Get-Content -LiteralPath $ctPath -Raw
if ($null -eq $xml.CheatTable) { throw 'Patched CT failed XML validation.' }
$raw = Get-Content -LiteralPath $ctPath -Raw
foreach ($required in @(
    '🎣 All Fish Are Crates (vanilla rarity rolls preserved)',
    'x64ctEnableAllFishAreCrates()',
    '🎁 Lucky Treasure Bags (all chance drops succeed, RNG preserved)',
    'x64ctEnableLuckyTreasureBags()',
    'x64ctDisableLuckyTreasureBags()'
)) {
    if (!$raw.Contains($required)) { throw "Patched CT is missing required marker: $required" }
}

Write-Host "Patched and validated $ctPath"
