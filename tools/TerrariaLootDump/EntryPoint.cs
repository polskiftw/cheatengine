using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;

namespace TerrariaLootDump
{
    public static class EntryPoint
    {
        private const int SchemaVersion = 1;
        private const int MaxRuleDepth = 24;
        private const int MaxEnumerableItems = 512;
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static int Run(string ignored)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrWhiteSpace(assemblyDir))
                assemblyDir = Path.GetTempPath();

            string outputDir = Path.Combine(assemblyDir, "dump");
            Directory.CreateDirectory(outputDir);

            try
            {
                File.WriteAllText(Path.Combine(outputDir, "STATUS.txt"), "Dump started at " + DateTime.UtcNow.ToString("O") + " UTC\r\n", Utf8);
                DumpEverything(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "STATUS.txt"), "DONE\r\n", Utf8);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(outputDir, "ERROR.txt"), ex.ToString(), Utf8);
                    File.WriteAllText(Path.Combine(outputDir, "STATUS.txt"), "FAILED - see ERROR.txt\r\n", Utf8);
                }
                catch { }
                return 1;
            }
        }

        private static void DumpEverything(string outputDir)
        {
            Assembly terrariaAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => SafeGetType(a, "Terraria.Main") != null);
            if (terrariaAssembly == null)
                throw new InvalidOperationException("Could not find the loaded Terraria assembly. Run this only after attaching Cheat Engine to Terraria.exe and reaching at least the title screen.");

            Type mainType = terrariaAssembly.GetType("Terraria.Main", true);
            Type npcIdType = terrariaAssembly.GetType("Terraria.ID.NPCID", true);
            Type langType = terrariaAssembly.GetType("Terraria.Lang", true);

            object itemDropsDb = GetStaticMember(mainType, "ItemDropsDB");
            if (itemDropsDb == null)
                throw new InvalidOperationException("Terraria.Main.ItemDropsDB was null. The loot database may not be initialized yet; reach the title screen/world and retry.");

            MethodInfo getRules = itemDropsDb.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "GetRulesForNPCID") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == typeof(int) && p[1].ParameterType == typeof(bool);
                });
            if (getRules == null)
                throw new MissingMethodException(itemDropsDb.GetType().FullName, "GetRulesForNPCID(int,bool)");

            int npcCount = Convert.ToInt32(GetStaticMember(npcIdType, "Count"), CultureInfo.InvariantCulture);
            if (npcCount <= 0 || npcCount > 100000)
                throw new InvalidOperationException("NPCID.Count looked invalid: " + npcCount);

            MethodInfo npcNameMethod = FindStaticIntMethod(langType, "GetNPCNameValue");
            MethodInfo itemNameMethod = FindStaticIntMethod(langType, "GetItemNameValue");

            var dump = new DumpRoot
            {
                schemaVersion = SchemaVersion,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                clrVersion = Environment.Version.ToString(),
                terrariaAssembly = terrariaAssembly.FullName,
                terrariaAssemblyVersion = terrariaAssembly.GetName().Version == null ? "" : terrariaAssembly.GetName().Version.ToString(),
                terrariaMainVersion = Convert.ToString(GetStaticMember(mainType, "versionNumber"), CultureInfo.InvariantCulture) ?? "",
                npcIdCount = npcCount,
                npcs = new List<NpcDump>(),
                warnings = new List<string>()
            };

            var serializer = NewSerializer();
            var ruleTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var flatRows = new List<FlatRuleRow>();
            var npcRows = new List<NpcSummaryRow>();

            for (int npcId = 0; npcId < npcCount; npcId++)
            {
                string npcName = GetName(npcNameMethod, npcId, "NPC " + npcId);
                List<object> specificRoots = ToObjectList(getRules.Invoke(itemDropsDb, new object[] { npcId, false }));
                List<object> allRoots = ToObjectList(getRules.Invoke(itemDropsDb, new object[] { npcId, true }));

                var specificSet = new HashSet<object>(specificRoots, ReferenceComparer.Instance);
                var npcDump = new NpcDump
                {
                    npcId = npcId,
                    npcName = npcName,
                    roots = new List<RuleNode>()
                };

                int rootIndex = 0;
                foreach (object root in allRoots)
                {
                    if (root == null) continue;
                    string scope = specificSet.Contains(root) ? "npc" : "global";
                    RuleNode node = BuildRuleNode(root, "root[" + rootIndex + "]", scope, 0, new HashSet<object>(ReferenceComparer.Instance));
                    npcDump.roots.Add(node);
                    FlattenRule(node, npcId, npcName, itemNameMethod, flatRows, ruleTypeCounts, serializer);
                    rootIndex++;
                }

                dump.npcs.Add(npcDump);
                npcRows.Add(new NpcSummaryRow
                {
                    npcId = npcId,
                    npcName = npcName,
                    rootRules = npcDump.roots.Count,
                    totalRuleNodes = CountNodes(npcDump.roots)
                });
            }

            string json = serializer.Serialize(dump);
            File.WriteAllText(Path.Combine(outputDir, "lootdump.json"), json, Utf8);
            WriteRulesCsv(Path.Combine(outputDir, "rules.csv"), flatRows);
            WriteNpcCsv(Path.Combine(outputDir, "npcs.csv"), npcRows);
            WriteRuleTypesCsv(Path.Combine(outputDir, "rule-types.csv"), ruleTypeCounts);

            var manifest = new StringBuilder();
            manifest.AppendLine("Terraria Loot Dump");
            manifest.AppendLine("===================");
            manifest.AppendLine("Generated UTC: " + dump.generatedUtc);
            manifest.AppendLine("CLR: " + dump.clrVersion);
            manifest.AppendLine("Terraria assembly: " + dump.terrariaAssembly);
            manifest.AppendLine("Terraria assembly version: " + dump.terrariaAssemblyVersion);
            manifest.AppendLine("Terraria Main.versionNumber: " + dump.terrariaMainVersion);
            manifest.AppendLine("NPCID.Count: " + dump.npcIdCount);
            manifest.AppendLine("NPC rows: " + npcRows.Count);
            manifest.AppendLine("Flattened rule nodes: " + flatRows.Count);
            manifest.AppendLine("Distinct runtime rule types: " + ruleTypeCounts.Count);
            manifest.AppendLine();
            manifest.AppendLine("Files:");
            manifest.AppendLine("  lootdump.json   Raw recursive rule tree with reflected runtime fields");
            manifest.AppendLine("  rules.csv       One row per discovered rule node");
            manifest.AppendLine("  npcs.csv        NPC summary counts");
            manifest.AppendLine("  rule-types.csv  Runtime rule-type frequency");
            manifest.AppendLine();
            manifest.AppendLine("This dumper does not call TryDroppingItem, NPCLoot, or any mutation method.");
            manifest.AppendLine("It only reads the already-built ItemDropsDB with GetRulesForNPCID and reflection.");
            File.WriteAllText(Path.Combine(outputDir, "MANIFEST.txt"), manifest.ToString(), Utf8);
        }

        private static RuleNode BuildRuleNode(object rule, string relation, string scope, int depth, HashSet<object> ancestors)
        {
            var node = new RuleNode
            {
                relation = relation,
                scope = scope,
                ruleType = rule.GetType().FullName,
                values = CaptureValues(rule),
                children = new List<RuleNode>()
            };

            if (depth >= MaxRuleDepth)
            {
                node.values["__truncated_depth"] = true;
                return node;
            }

            if (ancestors.Contains(rule))
            {
                node.values["__cycle"] = true;
                return node;
            }

            ancestors.Add(rule);
            try
            {
                var seenChildren = new HashSet<object>(ReferenceComparer.Instance);
                foreach (FieldInfo field in GetAllFields(rule.GetType()))
                {
                    if (field.IsStatic) continue;
                    object value;
                    try { value = field.GetValue(rule); }
                    catch { continue; }
                    if (value == null) continue;

                    string memberName = CleanName(field.Name);
                    AddRuleChildrenFromValue(node.children, seenChildren, value, memberName, scope, depth + 1, ancestors);
                }
            }
            finally
            {
                ancestors.Remove(rule);
            }
            return node;
        }

        private static void AddRuleChildrenFromValue(List<RuleNode> output, HashSet<object> seen, object value, string relation, string scope, int depth, HashSet<object> ancestors)
        {
            if (IsRule(value))
            {
                if (seen.Add(value))
                    output.Add(BuildRuleNode(value, relation, scope, depth, ancestors));
                return;
            }

            if (IsChainAttempt(value))
            {
                AddRulesFromChainAttempt(output, seen, value, relation, scope, depth, ancestors);
                return;
            }

            if (value is string) return;
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null) return;

            int i = 0;
            foreach (object item in enumerable)
            {
                if (i >= MaxEnumerableItems) break;
                if (item != null)
                {
                    string indexed = relation + "[" + i + "]";
                    if (IsRule(item))
                    {
                        if (seen.Add(item))
                            output.Add(BuildRuleNode(item, indexed, scope, depth, ancestors));
                    }
                    else if (IsChainAttempt(item))
                    {
                        AddRulesFromChainAttempt(output, seen, item, indexed, scope, depth, ancestors);
                    }
                }
                i++;
            }
        }

        private static void AddRulesFromChainAttempt(List<RuleNode> output, HashSet<object> seen, object chain, string relation, string scope, int depth, HashSet<object> ancestors)
        {
            string chainType = chain.GetType().Name;
            foreach (FieldInfo field in GetAllFields(chain.GetType()))
            {
                if (field.IsStatic) continue;
                object value;
                try { value = field.GetValue(chain); }
                catch { continue; }
                if (value == null) continue;
                if (IsRule(value) && seen.Add(value))
                {
                    string via = relation + "(" + chainType + ")/" + CleanName(field.Name);
                    RuleNode child = BuildRuleNode(value, via, scope, depth, ancestors);
                    child.values["__chainAttemptType"] = chain.GetType().FullName;
                    Dictionary<string, object> chainValues = CaptureValues(chain);
                    if (chainValues.Count > 0) child.values["__chainAttemptValues"] = chainValues;
                    output.Add(child);
                }
            }
        }

        private static Dictionary<string, object> CaptureValues(object obj)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (FieldInfo field in GetAllFields(obj.GetType()))
            {
                if (field.IsStatic) continue;
                object value;
                try { value = field.GetValue(obj); }
                catch { continue; }
                if (value == null) continue;

                object simple;
                if (!TryMakeSerializable(value, 0, out simple)) continue;
                string name = CleanName(field.Name);
                if (result.ContainsKey(name)) name = field.DeclaringType.Name + "." + name;
                result[name] = simple;
            }
            return result;
        }

        private static bool TryMakeSerializable(object value, int depth, out object serializable)
        {
            serializable = null;
            if (value == null) return true;
            Type t = value.GetType();

            if (value is string)
            {
                string s = (string)value;
                serializable = s.Length <= 1000 ? s : s.Substring(0, 1000) + "...";
                return true;
            }
            if (t.IsEnum)
            {
                serializable = value.ToString();
                return true;
            }
            if (t.IsPrimitive || value is decimal)
            {
                serializable = value;
                return true;
            }
            if (value is Type)
            {
                serializable = ((Type)value).FullName;
                return true;
            }
            if (IsRule(value) || IsChainAttempt(value)) return false;

            if (depth <= 1 && value is IEnumerable && !(value is string))
            {
                var list = new List<object>();
                int count = 0;
                foreach (object item in (IEnumerable)value)
                {
                    if (count++ >= MaxEnumerableItems) break;
                    object child;
                    if (!TryMakeSerializable(item, depth + 1, out child)) return false;
                    list.Add(child);
                }
                serializable = list;
                return true;
            }

            if (depth <= 1 && (t.IsValueType || LooksLikeCondition(t)))
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                dict["__type"] = t.FullName;
                foreach (FieldInfo field in GetAllFields(t))
                {
                    if (field.IsStatic) continue;
                    object fv;
                    try { fv = field.GetValue(value); }
                    catch { continue; }
                    object child;
                    if (fv != null && TryMakeSerializable(fv, depth + 1, out child))
                        dict[CleanName(field.Name)] = child;
                }
                serializable = dict;
                return true;
            }

            return false;
        }

        private static bool LooksLikeCondition(Type t)
        {
            if (t == null) return false;
            if (t.Name.IndexOf("Condition", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return t.GetInterfaces().Any(i => i.FullName == "Terraria.GameContent.ItemDropRules.IItemDropRuleCondition");
        }

        private static bool IsRule(object obj)
        {
            if (obj == null) return false;
            return obj.GetType().GetInterfaces().Any(i => i.FullName == "Terraria.GameContent.ItemDropRules.IItemDropRule");
        }

        private static bool IsChainAttempt(object obj)
        {
            if (obj == null) return false;
            Type t = obj.GetType();
            if (t.GetInterfaces().Any(i => i.FullName == "Terraria.GameContent.ItemDropRules.IItemDropRuleChainAttempt")) return true;
            return t.Name.IndexOf("Chain", StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Attempt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(AnyInstance))
                    yield return f;
            }
        }

        private static string CleanName(string name)
        {
            if (name != null && name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">k__BackingField", StringComparison.Ordinal))
            {
                int end = name.IndexOf('>');
                if (end > 1) return name.Substring(1, end - 1);
            }
            return name ?? "";
        }

        private static object GetStaticMember(Type type, string name)
        {
            FieldInfo f = type.GetField(name, AnyStatic);
            if (f != null)
            {
                if (f.IsLiteral) return f.GetRawConstantValue();
                return f.GetValue(null);
            }
            PropertyInfo p = type.GetProperty(name, AnyStatic);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(null, null);
            return null;
        }

        private static MethodInfo FindStaticIntMethod(Type type, string name)
        {
            return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
        }

        private static string GetName(MethodInfo method, int id, string fallback)
        {
            if (method == null) return fallback;
            try
            {
                object value = method.Invoke(null, new object[] { id });
                string s = value as string;
                if (!string.IsNullOrWhiteSpace(s)) return s;
                if (value != null)
                {
                    PropertyInfo p = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                    {
                        s = p.GetValue(value, null) as string;
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }
            catch { }
            return fallback;
        }

        private static Type SafeGetType(Assembly assembly, string name)
        {
            try { return assembly.GetType(name, false); }
            catch { return null; }
        }

        private static List<object> ToObjectList(object value)
        {
            var result = new List<object>();
            IEnumerable e = value as IEnumerable;
            if (e == null) return result;
            foreach (object item in e) result.Add(item);
            return result;
        }

        private static JavaScriptSerializer NewSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
        }

        private static void FlattenRule(RuleNode node, int npcId, string npcName, MethodInfo itemNameMethod, List<FlatRuleRow> rows, Dictionary<string, int> counts, JavaScriptSerializer serializer)
        {
            int current;
            counts.TryGetValue(node.ruleType ?? "", out current);
            counts[node.ruleType ?? ""] = current + 1;

            int? itemId = FindInt(node.values, "itemId", "itemID", "itemType");
            List<int> optionIds = FindIntList(node.values, "dropIds", "itemIds", "options");
            string itemIds = itemId.HasValue ? itemId.Value.ToString(CultureInfo.InvariantCulture) : string.Join(";", optionIds.Select(x => x.ToString(CultureInfo.InvariantCulture)));
            string itemNames = itemId.HasValue
                ? GetName(itemNameMethod, itemId.Value, "Item " + itemId.Value)
                : string.Join(";", optionIds.Select(x => GetName(itemNameMethod, x, "Item " + x)));

            rows.Add(new FlatRuleRow
            {
                npcId = npcId,
                npcName = npcName,
                scope = node.scope,
                path = node.relation,
                ruleType = node.ruleType,
                itemIds = itemIds,
                itemNames = itemNames,
                chanceNumerator = FindString(node.values, "chanceNumerator"),
                chanceDenominator = FindString(node.values, "chanceDenominator"),
                minAmount = FindString(node.values, "amountDroppedMinimum", "minimumDropped", "minimumStack"),
                maxAmount = FindString(node.values, "amountDroppedMaximum", "maximumDropped", "maximumStack"),
                rerolls = FindString(node.values, "rerolls", "rerollCount"),
                condition = FindCondition(node.values),
                valuesJson = serializer.Serialize(node.values)
            });

            foreach (RuleNode child in node.children)
                FlattenRule(child, npcId, npcName, itemNameMethod, rows, counts, serializer);
        }

        private static int CountNodes(IEnumerable<RuleNode> nodes)
        {
            int n = 0;
            foreach (RuleNode node in nodes)
            {
                n++;
                n += CountNodes(node.children);
            }
            return n;
        }

        private static int? FindInt(Dictionary<string, object> values, params string[] names)
        {
            foreach (string name in names)
            {
                object v = FindValue(values, name);
                if (v == null) continue;
                try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
                catch { }
            }
            return null;
        }

        private static List<int> FindIntList(Dictionary<string, object> values, params string[] names)
        {
            foreach (string name in names)
            {
                object v = FindValue(values, name);
                IEnumerable e = v as IEnumerable;
                if (e == null || v is string) continue;
                var list = new List<int>();
                foreach (object x in e)
                {
                    try { list.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture)); }
                    catch { }
                }
                if (list.Count > 0) return list;
            }
            return new List<int>();
        }

        private static string FindString(Dictionary<string, object> values, params string[] names)
        {
            foreach (string name in names)
            {
                object v = FindValue(values, name);
                if (v != null) return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            }
            return "";
        }

        private static object FindValue(Dictionary<string, object> values, string wanted)
        {
            foreach (KeyValuePair<string, object> kv in values)
            {
                string key = kv.Key;
                int dot = key.LastIndexOf('.');
                if (dot >= 0) key = key.Substring(dot + 1);
                if (string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            return null;
        }

        private static string FindCondition(Dictionary<string, object> values)
        {
            foreach (KeyValuePair<string, object> kv in values)
            {
                if (kv.Key.IndexOf("condition", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var d = kv.Value as IDictionary<string, object>;
                if (d != null && d.ContainsKey("__type")) return Convert.ToString(d["__type"], CultureInfo.InvariantCulture) ?? "";
                if (kv.Value != null) return kv.Value.ToString();
            }
            return "";
        }

        private static void WriteRulesCsv(string path, List<FlatRuleRow> rows)
        {
            using (var w = new StreamWriter(path, false, Utf8))
            {
                WriteCsvRow(w, "npc_id", "npc_name", "scope", "path", "rule_type", "item_ids", "item_names", "chance_numerator", "chance_denominator", "min_amount", "max_amount", "rerolls", "condition", "values_json");
                foreach (FlatRuleRow r in rows)
                    WriteCsvRow(w, r.npcId.ToString(CultureInfo.InvariantCulture), r.npcName, r.scope, r.path, r.ruleType, r.itemIds, r.itemNames, r.chanceNumerator, r.chanceDenominator, r.minAmount, r.maxAmount, r.rerolls, r.condition, r.valuesJson);
            }
        }

        private static void WriteNpcCsv(string path, List<NpcSummaryRow> rows)
        {
            using (var w = new StreamWriter(path, false, Utf8))
            {
                WriteCsvRow(w, "npc_id", "npc_name", "root_rules", "total_rule_nodes");
                foreach (NpcSummaryRow r in rows)
                    WriteCsvRow(w, r.npcId.ToString(CultureInfo.InvariantCulture), r.npcName, r.rootRules.ToString(CultureInfo.InvariantCulture), r.totalRuleNodes.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void WriteRuleTypesCsv(string path, Dictionary<string, int> counts)
        {
            using (var w = new StreamWriter(path, false, Utf8))
            {
                WriteCsvRow(w, "rule_type", "node_count");
                foreach (KeyValuePair<string, int> kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                    WriteCsvRow(w, kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void WriteCsvRow(StreamWriter w, params string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i != 0) w.Write(',');
                string s = fields[i] ?? "";
                w.Write('"');
                w.Write(s.Replace("\"", "\"\""));
                w.Write('"');
            }
            w.WriteLine();
        }

        private sealed class DumpRoot
        {
            public int schemaVersion;
            public string generatedUtc;
            public string clrVersion;
            public string terrariaAssembly;
            public string terrariaAssemblyVersion;
            public string terrariaMainVersion;
            public int npcIdCount;
            public List<NpcDump> npcs;
            public List<string> warnings;
        }

        private sealed class NpcDump
        {
            public int npcId;
            public string npcName;
            public List<RuleNode> roots;
        }

        private sealed class RuleNode
        {
            public string relation;
            public string scope;
            public string ruleType;
            public Dictionary<string, object> values;
            public List<RuleNode> children;
        }

        private sealed class FlatRuleRow
        {
            public int npcId;
            public string npcName;
            public string scope;
            public string path;
            public string ruleType;
            public string itemIds;
            public string itemNames;
            public string chanceNumerator;
            public string chanceDenominator;
            public string minAmount;
            public string maxAmount;
            public string rerolls;
            public string condition;
            public string valuesJson;
        }

        private sealed class NpcSummaryRow
        {
            public int npcId;
            public string npcName;
            public int rootRules;
            public int totalRuleNodes;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
