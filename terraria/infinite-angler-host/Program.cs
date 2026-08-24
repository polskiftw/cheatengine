using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InfiniteAnglerHost;

internal static class Program
{
    const string Marker = "InfiniteAnglerHost.__InfiniteAnglerHostPatchMarker";
    const string PersonalMarker = "InfiniteAngler.__InfiniteAnglerPatchMarker";
    const string ManifestName = "InfiniteAnglerHost.manifest.json";

    sealed record Manifest(string TargetFile, string BackupFile, string OriginalSha256, string PatchedSha256, string AssemblyVersion, DateTimeOffset PatchedAtUtc);

    sealed record Plan(
        AssemblyDefinition Assembly, ModuleDefinition Module, TypeDefinition MainType,
        MethodDefinition CompletionHandler, Instruction AddCall, MethodDefinition Swap,
        MethodDefinition SendData, MethodDefinition FromLiteral,
        FieldDefinition NetMode, FieldDefinition AnglerQuest, FieldDefinition FinishedToday,
        FieldDefinition PlayerArray, FieldDefinition PlayerName, FieldDefinition WhoAmI,
        DefaultAssemblyResolver Resolver) : IDisposable
    {
        public void Dispose() { Assembly.Dispose(); Resolver.Dispose(); }
    }

    static int Main(string[] args)
    {
        try
        {
            string command = args.Any(x => x.Equals("--restore", StringComparison.OrdinalIgnoreCase)) ? "restore" :
                             args.Any(x => x.Equals("--check", StringComparison.OrdinalIgnoreCase)) ? "check" : "install";
            string target = ResolveTarget(args);
            string manifest = Path.Combine(Path.GetDirectoryName(target)!, ManifestName);
            Console.WriteLine("Infinite Angler Host - Option A (host only)");
            Console.WriteLine($"Target: {target}");
            return command switch { "restore" => Restore(target, manifest), "check" => Check(target), _ => Install(target, manifest) };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            if (Environment.GetEnvironmentVariable("CI") is not null) Console.Error.WriteLine(ex);
            Console.Error.WriteLine("No intentional replacement of Terraria.exe was performed after this failure.");
            return 1;
        }
    }

    static string ResolveTarget(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--target", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(args[i + 1]);
        foreach (var p in new[] { Path.Combine(Environment.CurrentDirectory, "Terraria.exe"), Path.Combine(AppContext.BaseDirectory, "Terraria.exe") })
            if (File.Exists(p)) return Path.GetFullPath(p);
        throw new FileNotFoundException("Put InfiniteAnglerHost.exe beside Terraria.exe, or use --target \"C:\\...\\Terraria.exe\".");
    }

    static int Check(string target)
    {
        EnsureTarget(target);
        using var p = BuildPlan(target);
        Console.WriteLine($"Assembly: {p.Assembly.Name.Version}");
        Console.WriteLine($"Completion handler: {p.CompletionHandler.FullName}");
        Console.WriteLine($"Packet sender: {p.SendData.FullName}");
        Console.WriteLine(HasType(p.Module, Marker) ? "Status: already patched." : "Status: compatible host-side structural match found.");
        Console.WriteLine("Compatibility is structural; it is not hard-locked to 1.4.5.8.");
        return 0;
    }

    static int Install(string target, string manifestPath)
    {
        EnsureTarget(target);
        using var p = BuildPlan(target);
        if (HasType(p.Module, PersonalMarker))
            throw new InvalidOperationException("Option B is installed in this Terraria.exe. Restore Option B before installing Option A.");
        if (HasType(p.Module, Marker)) { Console.WriteLine("Already patched."); return 0; }

        string root = Path.GetDirectoryName(target)!;
        string originalHash = Hash(target);
        string backupDir = Path.Combine(root, "InfiniteAnglerHost-backups");
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, $"Terraria.{originalHash[..12]}.original.exe");
        if (!File.Exists(backup)) File.Copy(target, backup);

        Apply(p);
        string tmp = target + ".InfiniteAnglerHost.tmp";
        try
        {
            p.Assembly.Write(tmp, new WriterParameters { WriteSymbols = false });
            using (var verify = AssemblyDefinition.ReadAssembly(tmp, new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Deferred, AssemblyResolver = p.Resolver }))
                if (!HasType(verify.MainModule, Marker)) throw new InvalidOperationException("Patched-file verification failed.");
            File.Move(tmp, target, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }

        string patchedHash = Hash(target);
        var m = new Manifest(Path.GetFileName(target), Path.GetRelativePath(root, backup), originalHash, patchedHash,
            p.Assembly.Name.Version?.ToString() ?? "unknown", DateTimeOffset.UtcNow);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PATCHED. Only the Host & Play PC needs this. Guests remain vanilla.");
        Console.WriteLine($"Backup: {backup}");
        return 0;
    }

    static int Restore(string target, string manifestPath)
    {
        EnsureTarget(target);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("InfiniteAnglerHost.manifest.json was not found.");
        var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("Manifest is unreadable.");
        if (!Hash(target).Equals(m.PatchedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Terraria.exe changed after patching; refusing to overwrite it with an older backup.");
        string root = Path.GetDirectoryName(target)!;
        string backup = Path.GetFullPath(Path.Combine(root, m.BackupFile));
        if (!File.Exists(backup) || !Hash(backup).Equals(m.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Matching original backup is missing or has the wrong hash.");
        File.Copy(backup, target, true);
        File.Delete(manifestPath);
        Console.WriteLine("RESTORED vanilla Terraria.exe.");
        return 0;
    }

    static Plan BuildPlan(string target)
    {
        var resolver = Resolver(target);
        AssemblyDefinition asm;
        try { asm = AssemblyDefinition.ReadAssembly(target, new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Deferred, AssemblyResolver = resolver }); }
        catch { resolver.Dispose(); throw; }
        try
        {
            var mod = asm.MainModule;
            var main = Type(mod, "Terraria.Main") ?? Fail<TypeDefinition>("Terraria.Main missing");
            var mb = Type(mod, "Terraria.MessageBuffer") ?? Fail<TypeDefinition>("Terraria.MessageBuffer missing");
            var player = Type(mod, "Terraria.Player") ?? Fail<TypeDefinition>("Terraria.Player missing");
            var net = Type(mod, "Terraria.NetMessage") ?? Fail<TypeDefinition>("Terraria.NetMessage missing");
            var nt = Type(mod, "Terraria.Localization.NetworkText") ?? Fail<TypeDefinition>("NetworkText missing");

            var netMode = Field(main, "netMode");
            var quest = Field(main, "anglerQuest");
            var finished = Field(main, "anglerWhoFinishedToday");
            var players = Field(main, "player");
            var pname = Field(player, "name");
            var who = Field(mb, "whoAmI");
            if (netMode.FieldType.MetadataType != MetadataType.Int32 || quest.FieldType.MetadataType != MetadataType.Int32 ||
                who.FieldType.MetadataType != MetadataType.Int32 || pname.FieldType.MetadataType != MetadataType.String)
                Fail<object>("core Angler/network field types changed");

            var swap = main.Methods.SingleOrDefault(x => x.Name == "AnglerQuestSwap" && x.IsStatic && !x.HasParameters && x.ReturnType.MetadataType == MetadataType.Void)
                       ?? Fail<MethodDefinition>("Main.AnglerQuestSwap() missing");
            var fromLiteral = nt.Methods.SingleOrDefault(x => x.Name == "FromLiteral" && x.IsStatic && x.Parameters.Count == 1 &&
                                                         x.Parameters[0].ParameterType.MetadataType == MetadataType.String && x.ReturnType.FullName == nt.FullName)
                              ?? Fail<MethodDefinition>("NetworkText.FromLiteral(string) missing");
            var sends = net.Methods.Where(x => x.Name == "SendData" && x.IsStatic && x.ReturnType.MetadataType == MetadataType.Void &&
                                               x.Parameters.Count >= 4 && x.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
                                               x.Parameters[1].ParameterType.MetadataType == MetadataType.Int32 && x.Parameters[2].ParameterType.MetadataType == MetadataType.Int32 &&
                                               x.Parameters[3].ParameterType.FullName == nt.FullName).ToArray();
            if (sends.Length != 1) Fail<object>($"expected one NetMessage.SendData overload, found {sends.Length}");
            var send = sends[0];
            if (!Refs(send, quest) || !Refs(send, finished)) Fail<object>("packet serializer no longer references Angler quest + completion list");
            for (int i = 4; i < send.Parameters.Count; i++)
                if (send.Parameters[i].ParameterType.MetadataType is not (MetadataType.Int32 or MetadataType.Single))
                    Fail<object>($"unsupported SendData parameter type {send.Parameters[i].ParameterType.FullName}");

            var candidates = new List<(MethodDefinition Method, Instruction Add, int Score)>();
            foreach (var method in mb.Methods.Where(x => x.HasBody))
            {
                var ins = method.Body.Instructions;
                for (int i = 0; i < ins.Count; i++)
                {
                    if (!IsListAdd(ins[i]) || !Nearby(ins, i, finished, 10)) continue;
                    int score = (Refs(method, netMode) ? 2 : 0) + (Refs(method, who) ? 4 : 0) +
                                (Refs(method, players) ? 2 : 0) + (Refs(method, pname) ? 2 : 0);
                    candidates.Add((method, ins[i], score));
                }
            }
            if (candidates.Count == 0) Fail<object>("server Angler completion-name Add path not found");
            int bestScore = candidates.Max(x => x.Score);
            var best = candidates.Where(x => x.Score == bestScore).ToArray();
            if (bestScore < 6 || best.Length != 1) Fail<object>("server Angler completion path was not uniquely identifiable");

            return new Plan(asm, mod, main, best[0].Method, best[0].Add, swap, send, fromLiteral,
                netMode, quest, finished, players, pname, who, resolver);
        }
        catch { asm.Dispose(); resolver.Dispose(); throw; }
    }

    static void Apply(Plan p)
    {
        p.Module.Types.Add(new TypeDefinition("InfiniteAnglerHost", "__InfiniteAnglerHostPatchMarker",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, p.Module.TypeSystem.Object));
        var helper = Helper(p);
        var il = p.CompletionHandler.Body.GetILProcessor();
        var next = p.AddCall.Next;
        var a = il.Create(OpCodes.Ldarg_0);
        var b = il.Create(OpCodes.Ldfld, p.WhoAmI);
        var c = il.Create(OpCodes.Call, helper);
        if (next is null) { il.Append(a); il.Append(b); il.Append(c); }
        else { il.InsertBefore(next, a); il.InsertBefore(next, b); il.InsertBefore(next, c); }
    }

    static MethodDefinition Helper(Plan p)
    {
        var h = new MethodDefinition("__InfiniteAnglerHost_OnQuestCompleted",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, p.Module.TypeSystem.Void);
        h.Parameters.Add(new ParameterDefinition("whoAmI", ParameterAttributes.None, p.Module.TypeSystem.Int32));
        h.Body.InitLocals = true;
        var oldQuest = new VariableDefinition(p.Module.TypeSystem.Int32);
        var oldMode = new VariableDefinition(p.Module.TypeSystem.Int32);
        var name = new VariableDefinition(p.Module.TypeSystem.String);
        h.Body.Variables.Add(oldQuest); h.Body.Variables.Add(oldMode); h.Body.Variables.Add(name);

        var remove = new MethodReference("Remove", p.Module.TypeSystem.Boolean, p.Module.ImportReference(p.FinishedToday.FieldType)) { HasThis = true };
        remove.Parameters.Add(new ParameterDefinition(p.Module.TypeSystem.String));
        var il = h.Body.GetILProcessor();
        var ret = il.Create(OpCodes.Ret);

        il.Append(il.Create(OpCodes.Ldsfld, p.NetMode)); il.Append(il.Create(OpCodes.Ldc_I4_2)); il.Append(il.Create(OpCodes.Bne_Un, ret));
        il.Append(il.Create(OpCodes.Ldsfld, p.PlayerArray)); il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Ldfld, p.PlayerName)); il.Append(il.Create(OpCodes.Stloc, name));
        il.Append(il.Create(OpCodes.Ldsfld, p.FinishedToday)); il.Append(il.Create(OpCodes.Ldloc, name)); il.Append(il.Create(OpCodes.Callvirt, remove)); il.Append(il.Create(OpCodes.Pop));

        il.Append(il.Create(OpCodes.Ldsfld, p.AnglerQuest)); il.Append(il.Create(OpCodes.Stloc, oldQuest));
        il.Append(il.Create(OpCodes.Ldsfld, p.NetMode)); il.Append(il.Create(OpCodes.Stloc, oldMode));
        il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Stsfld, p.NetMode)); il.Append(il.Create(OpCodes.Call, p.Swap));
        il.Append(il.Create(OpCodes.Ldloc, oldMode)); il.Append(il.Create(OpCodes.Stsfld, p.NetMode));

        il.Append(il.Create(OpCodes.Ldc_I4, 74)); il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldc_I4_M1));
        il.Append(il.Create(OpCodes.Ldloc, name)); il.Append(il.Create(OpCodes.Call, p.FromLiteral));
        for (int i = 4; i < p.SendData.Parameters.Count; i++)
        {
            if (p.SendData.Parameters[i].ParameterType.MetadataType == MetadataType.Int32) il.Append(il.Create(OpCodes.Ldc_I4_0));
            else il.Append(il.Create(OpCodes.Ldc_R4, 0f));
        }
        il.Append(il.Create(OpCodes.Call, p.SendData));
        il.Append(il.Create(OpCodes.Ldloc, oldQuest)); il.Append(il.Create(OpCodes.Stsfld, p.AnglerQuest));
        il.Append(ret);
        p.MainType.Methods.Add(h);
        return h;
    }

    static bool IsListAdd(Instruction i)
    {
        if (i.OpCode.Code is not (Code.Call or Code.Callvirt) || i.Operand is not MethodReference m || m.Name != "Add" || m.Parameters.Count != 1) return false;
        return m.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal);
    }

    static bool Nearby(Mono.Collections.Generic.Collection<Instruction> ins, int at, FieldDefinition f, int radius)
    {
        for (int i = Math.Max(0, at - radius); i <= at; i++)
            if (ins[i].Operand is FieldReference fr && fr.FullName == f.FullName) return true;
        return false;
    }

    static bool Refs(MethodDefinition m, FieldDefinition f) => m.HasBody && m.Body.Instructions.Any(i => i.Operand is FieldReference fr && fr.FullName == f.FullName);
    static FieldDefinition Field(TypeDefinition t, string n) => t.Fields.SingleOrDefault(x => x.Name == n) ?? Fail<FieldDefinition>($"{t.FullName}.{n} missing");
    static TypeDefinition? Type(ModuleDefinition m, string n) => Types(m).FirstOrDefault(x => x.FullName == n);
    static IEnumerable<TypeDefinition> Types(ModuleDefinition m) { foreach (var t in m.Types) foreach (var x in Nested(t)) yield return x; }
    static IEnumerable<TypeDefinition> Nested(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes) foreach (var x in Nested(n)) yield return x; }
    static bool HasType(ModuleDefinition m, string n) => Type(m, n) is not null;
    static T Fail<T>(string s) => throw new InvalidOperationException("Terraria compatibility check failed: " + s + ". No changes were made.");

    static DefaultAssemblyResolver Resolver(string target)
    {
        var r = new DefaultAssemblyResolver();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? d) { if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) { d = Path.GetFullPath(d); if (seen.Add(d)) r.AddSearchDirectory(d); } }
        Add(Path.GetDirectoryName(target)); Add(AppContext.BaseDirectory); try { Add(RuntimeEnvironment.GetRuntimeDirectory()); } catch { }
        return r;
    }

    static void EnsureTarget(string p) { if (!File.Exists(p)) throw new FileNotFoundException("Target file not found.", p); }
    static string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
}
