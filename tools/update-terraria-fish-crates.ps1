$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$ctPath = Join-Path $repo 'Tables/Terraria/Terraria-1.4.5.x-Table-Ver-7-CE45-x64-CoreCLR-Experimental.CT'
if (!(Test-Path $ctPath)) { throw "Terraria x64 CT not found: $ctPath" }

$text = Get-Content -LiteralPath $ctPath -Raw

$oldEntry = @'
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

$newEntry = @'
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

if ($text.Contains($oldEntry)) {
    $text = $text.Replace($oldEntry, $newEntry)
} elseif (!$text.Contains('x64ctEnableAllFishAreCrates()')) {
    throw 'Could not locate the All Fish Are Crates TODO entry.'
}

$stateMarker = 'x64ct.rerolls = x64ct.rerolls or {}'
if (!$text.Contains('x64ct.fishCratesEnabled = x64ct.fishCratesEnabled or false')) {
    if (!$text.Contains($stateMarker)) { throw 'Could not locate x64ct state initialization marker.' }
    $text = $text.Replace($stateMarker, $stateMarker + "`r`nx64ct.fishCratesEnabled = x64ct.fishCratesEnabled or false")
}

$cleanupMarker = "  pcall(x64ctDisableNpcImmortal)`r`n  pcall(x64ctDisableMaxStackHotkey)"
if (!$text.Contains('pcall(x64ctDisableAllFishAreCrates,true)')) {
    if (!$text.Contains($cleanupMarker)) { throw 'Could not locate feature cleanup marker.' }
    $text = $text.Replace($cleanupMarker, "  pcall(x64ctDisableNpcImmortal)`r`n  pcall(x64ctDisableAllFishAreCrates,true)`r`n  pcall(x64ctDisableMaxStackHotkey)")
}

$luaMarker = @'
-- --------------------------------------------------------------------------
-- Buffs and debuff cleaners
-- --------------------------------------------------------------------------
'@

$fishLua = @'
-- --------------------------------------------------------------------------
-- All Fish Are Crates. CE injects a tiny managed helper into gloader's real
-- CoreCLR process. Harmony adds a postfix to FishingCheck_RollDropLevels and
-- changes only its final ref/out bool after vanilla has completed every other
-- rarity/quality roll.
-- --------------------------------------------------------------------------
local function x64ctFileExists(path)
  local f=io.open(path,'rb')
  if f then f:close(); return true end
  return false
end

local function x64ctTrailingSlash(path)
  local last=string.sub(path,-1)
  if last~='\\' and last~='/' then return path..'\\' end
  return path
end

function x64ctTableRoot()
  local origin=rawget(_G,'TrainerOrigin')
  if origin and origin~='' then return x64ctTrailingSlash(origin) end

  local candidate=x64ctTrailingSlash(getCheatEngineDir())..'Tables\\Terraria\\'
  if x64ctFileExists(candidate..'runtime\\TerrariaCEHelper.dll') then return candidate end

  error('Could not locate the Terraria CT runtime folder. Open the .CT from its Tables\\Terraria folder (double-clicking it is fine).')
end

function x64ctHelperCommand(command)
  x64ctEnsureDotNet()
  if type(injectDotNetDLL)~='function' then
    dofile(getCheatEngineDir()..'autorun\\DotNetInject.lua')
  end
  if type(injectDotNetDLL)~='function' then
    error('Cheat Engine DotNetInject.lua did not expose injectDotNetDLL.')
  end

  local runtime=x64ctTableRoot()..'runtime\\'
  local helper=runtime..'TerrariaCEHelper.dll'
  local harmony=runtime..'0Harmony.dll'
  if not x64ctFileExists(helper) then error('Missing '..helper) end
  if not x64ctFileExists(harmony) then error('Missing '..harmony) end

  local returnValue,injectError=injectDotNetDLL(helper,'TerrariaCEHelper.EntryPoint','Run',command)
  if returnValue==nil then
    error('Managed Terraria CE helper injection failed: '..tostring(injectError))
  end
  if returnValue~=23063 then
    error('Managed Terraria CE helper command '..command..' returned '..tostring(returnValue)..'.')
  end
  return true
end

function x64ctEnableAllFishAreCrates()
  x64ctHelperCommand('fish-on')
  x64ct.fishCratesEnabled=true
  print('[Terraria x64 CT] All Fish Are Crates enabled.')
end

function x64ctDisableAllFishAreCrates(silent)
  if not x64ct.fishCratesEnabled then return end
  local ok,err=pcall(x64ctHelperCommand,'fish-off')
  x64ct.fishCratesEnabled=false
  if not ok and not silent then error(err) end
  if ok then print('[Terraria x64 CT] All Fish Are Crates disabled.') end
end

'@

if (!$text.Contains('function x64ctEnableAllFishAreCrates()')) {
    if (!$text.Contains($luaMarker)) { throw 'Could not locate Lua feature insertion marker.' }
    $text = $text.Replace($luaMarker, $fishLua + $luaMarker)
}

$oldComments = 'Entries prefixed with x64 TODO are deliberately disabled.'
$newComments = 'Entries prefixed with x64 TODO are deliberately disabled. All Fish Are Crates uses the CE-injected managed helper in Tables/Terraria/runtime; it is not a gmod.'
if ($text.Contains($oldComments) -and !$text.Contains('All Fish Are Crates uses the CE-injected managed helper')) {
    $text = $text.Replace($oldComments, $newComments)
}

Set-Content -LiteralPath $ctPath -Value $text -Encoding utf8NoBOM

[xml]$xml = Get-Content -LiteralPath $ctPath -Raw
if ($null -eq $xml.CheatTable) { throw 'Patched CT failed XML validation.' }
if ((Get-Content -LiteralPath $ctPath -Raw) -notmatch '🎣 All Fish Are Crates \(vanilla rarity rolls preserved\)') {
    throw 'Patched CT does not contain the enabled fish-crates entry.'
}

Write-Host "Patched and validated $ctPath"
