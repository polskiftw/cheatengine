$ErrorActionPreference = 'Stop'
$p = Join-Path $PSScriptRoot 'EntryPoint.cs'
$s = Get-Content -Raw -Path $p

$old = @'
            int npcCount = Convert.ToInt32(GetStaticMember(npcIdType, "Count"), CultureInfo.InvariantCulture);
            if (npcCount <= 0 || npcCount > 100000)
                throw new InvalidOperationException("NPCID.Count looked invalid: " + npcCount);
'@
$new = @'
            int npcCount = Convert.ToInt32(GetStaticMember(npcIdType, "Count"), CultureInfo.InvariantCulture);
            if (npcCount <= 0 || npcCount > 100000)
                throw new InvalidOperationException("NPCID.Count looked invalid: " + npcCount);

            int negativeNpcStart = 0;
            object negativeCountValue = GetStaticMember(npcIdType, "NegativeIDCount");
            if (negativeCountValue != null)
            {
                negativeNpcStart = Convert.ToInt32(negativeCountValue, CultureInfo.InvariantCulture);
                if (negativeNpcStart > 0 || negativeNpcStart < -100000) negativeNpcStart = 0;
            }
'@
if (-not $s.Contains($old)) { throw 'Could not patch NPCID.Count block' }
$s = $s.Replace($old, $new)

$s = $s.Replace('            for (int npcId = 0; npcId < npcCount; npcId++)', '            for (int npcId = negativeNpcStart; npcId < npcCount; npcId++)')

$old = @'
            };

            if (depth >= MaxRuleDepth)
'@
$new = @'
            };

            node.values["__tryDroppingItemImpl"] = GetMethodImplementationOwner(rule.GetType(), "TryDroppingItem");
            node.values["__inheritance"] = GetInheritanceChain(rule.GetType());

            if (depth >= MaxRuleDepth)
'@
if (-not $s.Contains($old)) { throw 'Could not patch RuleNode metadata block' }
$s = $s.Replace($old, $new)

$marker = @'
        private static bool LooksLikeCondition(Type t)
'@
$helpers = @'
        private static string GetMethodImplementationOwner(Type type, string methodName)
        {
            try
            {
                MethodInfo m = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == methodName);
                return m == null || m.DeclaringType == null ? "" : m.DeclaringType.FullName;
            }
            catch { return ""; }
        }

        private static List<string> GetInheritanceChain(Type type)
        {
            var result = new List<string>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                result.Add(t.FullName);
            return result;
        }

        private static bool LooksLikeCondition(Type t)
'@
if (-not $s.Contains($marker)) { throw 'Could not add method/inheritance helpers' }
$s = $s.Replace($marker, $helpers)

$old = @'
            manifest.AppendLine("NPCID.Count: " + dump.npcIdCount);
'@
$new = @'
            manifest.AppendLine("NPCID.Count: " + dump.npcIdCount);
            manifest.AppendLine("NPCID.NegativeIDCount / first dumped net ID: " + negativeNpcStart);
'@
if (-not $s.Contains($old)) { throw 'Could not patch manifest NPC range' }
$s = $s.Replace($old, $new)

Set-Content -Path $p -Value $s -Encoding UTF8
Write-Host "Enhanced build source: negative NPC net IDs + TryDroppingItem implementation metadata"
