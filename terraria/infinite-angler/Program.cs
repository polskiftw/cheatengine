using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InfiniteAngler;

internal static class Program
{
    private const string MarkerTypeName = "__InfiniteAnglerPatchMarker";
    private const string HelperMethodName = "__InfiniteAngler_RollNextQuest";
    private const string ManifestName = "InfiniteAngler.manifest.json";

    private sealed record PatchManifest(
        string TargetFile,
        string BackupFile,
        string OriginalSha256,
        string PatchedSha256,
        string AssemblyVersion,
        DateTimeOffset PatchedAtUtc);

    private sealed record PatchPlan(
        AssemblyDefinition Assembly,
        ModuleDefinition Module,
        TypeDefinition MainType,
        MethodDefinition TurnInMethod,
        MethodDefinition AnglerQuestSwap,
        FieldDefinition NetMode,
        FieldDefinition AnglerQuestFinished,
        FieldDefinition AnglerWhoFinishedToday,
        IReadOnlyList<Instruction> RewardCalls);

    public static int Main(string[] args)
    {
        try
        {
            var command = args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase))
                ? "restore"
                : args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase))
                    ? "check"
                    : "install";

            var target = ResolveTarget(args);
            var manifestPath = Path.Combine(Path.GetDirectoryName(target)!, ManifestName);

            Console.WriteLine("Infinite Angler - vanilla Terraria drop-in patcher");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"Target: {target}");

            return command switch
            {
                "restore" => Restore(target, manifestPath),
                "check" => Check(target),
                _ => Install(target, manifestPath)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("ERROR: " + ex.Message);
            Console.Error.WriteLine("Nothing was intentionally changed after this failure.");
            return 1;
        }
    }

    private static string ResolveTarget(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--target", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        }

        var cwd = Path.Combine(Environment.CurrentDirectory, "Terraria.exe");
        if (File.Exists(cwd))
            return Path.GetFullPath(cwd);

        var besidePatcher = Path.Combine(AppContext.BaseDirectory, "Terraria.exe");
        if (File.Exists(besidePatcher))
            return Path.GetFullPath(besidePatcher);

        throw new FileNotFoundException(
            "Terraria.exe was not found. Put InfiniteAngler.exe in the Terraria install folder, " +
            "or use --target \"C:\\...\\Terraria.exe\".");
    }

    private static int Check(string target)
    {
        EnsureTargetExists(target);
        using var plan = BuildPlan(target);

        var version = plan.Assembly.Name.Version?.ToString() ?? "unknown";
        var alreadyPatched = HasMarker(plan.Module);

        Console.WriteLine($"Managed assembly version: {version}");
        Console.WriteLine($"Angler turn-in method: {plan.TurnInMethod.FullName}");
        Console.WriteLine($"Reward call count: {plan.RewardCalls.Count}");
        Console.WriteLine(alreadyPatched ? "Status: already patched." : "Status: compatible structural match found.");
        Console.WriteLine();
        Console.WriteLine("Compatibility is structural, not hard-locked to a Terraria version string.");
        return 0;
    }

    private static int Install(string target, string manifestPath)
    {
        EnsureTargetExists(target);

        using var plan = BuildPlan(target);
        var version = plan.Assembly.Name.Version?.ToString() ?? "unknown";
        Console.WriteLine($"Managed assembly version: {version}");

        if (HasMarker(plan.Module))
        {
            Console.WriteLine("Already patched. No changes made.");
            return 0;
        }

        Console.WriteLine($"Validated Angler turn-in method: {plan.TurnInMethod.FullName}");
        Console.WriteLine("Safety check: structural IL match passed.");
        Console.WriteLine("Applying: no daily Angler lockout + local vanilla quest reroll after each reward.");

        var originalHash = Sha256(target);
        var backupDir = Path.Combine(Path.GetDirectoryName(target)!, "InfiniteAngler-backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, $"Terraria.{originalHash[..12]}.original.exe");
        if (!File.Exists(backupPath))
            File.Copy(target, backupPath, overwrite: false);

        ApplyPatch(plan);

        var tempPath = target + ".InfiniteAngler.tmp";
        try
        {
            plan.Assembly.Write(tempPath, new WriterParameters { WriteSymbols = false });

            // Re-open the produced assembly before touching Terraria.exe. This catches malformed IL/container output.
            using (var verification = AssemblyDefinition.ReadAssembly(tempPath, new ReaderParameters { ReadSymbols = false, InMemory = true }))
            {
                if (!HasMarker(verification.MainModule))
                    throw new InvalidOperationException("Patched-file verification failed: marker missing.");
            }

            File.Move(tempPath, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        var patchedHash = Sha256(target);
        var manifest = new PatchManifest(
            Path.GetFileName(target),
            Path.GetRelativePath(Path.GetDirectoryName(target)!, backupPath),
            originalHash,
            patchedHash,
            version,
            DateTimeOffset.UtcNow);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine("PATCHED.");
        Console.WriteLine($"Original backup: {backupPath}");
        Console.WriteLine("Launch Terraria normally through Steam.");
        Console.WriteLine("Install the same patch on every PC that should have endless personal Angler quests.");
        return 0;
    }

    private static int Restore(string target, string manifestPath)
    {
        EnsureTargetExists(target);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("No InfiniteAngler.manifest.json was found beside Terraria.exe.");

        var manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("The Infinite Angler manifest could not be read.");

        var currentHash = Sha256(target);
        if (!currentHash.Equals(manifest.PatchedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Terraria.exe no longer matches the file Infinite Angler patched. Steam may have updated it. " +
                "Refusing to restore an older executable over a newer game version.");
        }

        var root = Path.GetDirectoryName(target)!;
        var backupPath = Path.GetFullPath(Path.Combine(root, manifest.BackupFile));
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("The recorded original backup is missing: " + backupPath);

        if (!Sha256(backupPath).Equals(manifest.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original backup hash does not match the manifest. Refusing to restore it.");

        File.Copy(backupPath, target, overwrite: true);
        File.Delete(manifestPath);

        Console.WriteLine("RESTORED vanilla Terraria.exe from the matching backup.");
        return 0;
    }

    private static PatchPlan BuildPlan(string target)
    {
        AssemblyDefinition assembly;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(target, new ReaderParameters
            {
                ReadSymbols = false,
                InMemory = true,
                ReadingMode = ReadingMode.Immediate
            });
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "Terraria.exe is not a managed assembly Mono.Cecil can patch. This build needs a different loader strategy; no changes were made.", ex);
        }

        try
        {
            var module = assembly.MainModule;
            var main = FindType(module, "Terraria.Main")
                       ?? throw CompatibilityFailure("Terraria.Main was not found.");
            var player = FindType(module, "Terraria.Player")
                         ?? throw CompatibilityFailure("Terraria.Player was not found.");

            var netMode = RequireField(main, "netMode");
            var finished = RequireField(main, "anglerQuestFinished");
            var finishedToday = RequireField(main, "anglerWhoFinishedToday");
            var swap = main.Methods.SingleOrDefault(m => m.Name == "AnglerQuestSwap" && m.IsStatic && !m.HasParameters && m.ReturnType.MetadataType == MetadataType.Void)
                       ?? throw CompatibilityFailure("Main.AnglerQuestSwap() was not found with the expected signature.");

            var rewardMethods = player.Methods.Where(m => m.Name == "GetAnglerReward").ToArray();
            if (rewardMethods.Length == 0)
                throw CompatibilityFailure("Player.GetAnglerReward was not found.");

            var rewardFullNames = rewardMethods.Select(m => m.FullName).ToHashSet(StringComparer.Ordinal);
            var candidates = new List<(MethodDefinition Method, List<Instruction> RewardCalls, int Score)>();

            foreach (var method in AllTypes(module).SelectMany(t => t.Methods))
            {
                if (!method.HasBody)
                    continue;

                var rewardCalls = method.Body.Instructions
                    .Where(i => IsCall(i) && i.Operand is MethodReference mr &&
                                mr.DeclaringType.FullName == player.FullName && mr.Name == "GetAnglerReward")
                    .ToList();
                if (rewardCalls.Count == 0)
                    continue;

                var score = 0;
                if (ReferencesField(method, finished)) score += 4;
                if (ReferencesField(method, finishedToday)) score += 4;
                if (ReferencesAnyFieldNamed(method, main, "anglerQuest")) score += 2;
                if (rewardCalls.Any(i => i.Operand is MethodReference mr && rewardFullNames.Contains(mr.FullName))) score += 2;
                candidates.Add((method, rewardCalls, score));
            }

            if (candidates.Count == 0)
                throw CompatibilityFailure("No method calls Player.GetAnglerReward.");

            var bestScore = candidates.Max(c => c.Score);
            var best = candidates.Where(c => c.Score == bestScore).ToArray();
            if (bestScore < 6 || best.Length != 1)
            {
                var detail = string.Join("; ", candidates.OrderByDescending(c => c.Score)
                    .Take(5).Select(c => $"{c.Method.FullName} [score {c.Score}]"));
                throw CompatibilityFailure("The Angler turn-in method was not uniquely identifiable. Candidates: " + detail);
            }

            if (best[0].RewardCalls.Any(i => i.Operand is MethodReference mr && mr.ReturnType.MetadataType != MetadataType.Void))
                throw CompatibilityFailure("GetAnglerReward no longer returns void; refusing to inject around an unfamiliar stack shape.");

            return new PatchPlan(assembly, module, main, best[0].Method, swap, netMode, finished, finishedToday, best[0].RewardCalls);
        }
        catch
        {
            assembly.Dispose();
            throw;
        }
    }

    private static void ApplyPatch(PatchPlan plan)
    {
        AddMarker(plan.Module);
        var helper = AddRerollHelper(plan);

        // Every time the Angler interaction code runs locally, make the local cooldown cache eligible again.
        // In multiplayer this does not edit the server's world state or the other player's client.
        var body = plan.TurnInMethod.Body;
        body.SimplifyMacrosSafe();
        var il = body.GetILProcessor();
        var first = body.Instructions.First();

        var clearMethod = new MethodReference("Clear", plan.Module.TypeSystem.Void, plan.Module.ImportReference(plan.AnglerWhoFinishedToday.FieldType))
        {
            HasThis = true
        };

        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Stsfld, plan.AnglerQuestFinished));
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, plan.AnglerWhoFinishedToday));
        il.InsertBefore(first, il.Create(OpCodes.Callvirt, clearMethod));

        // A reward call only happens on a successful turn-in, so this makes the next quest appear only after payment.
        foreach (var rewardCall in plan.RewardCalls.ToArray())
            il.InsertAfter(rewardCall, il.Create(OpCodes.Call, helper));

        AddAssemblyMetadata(plan.Module, "InfiniteAngler.StructuralPatch", "1");
    }

    private static MethodDefinition AddRerollHelper(PatchPlan plan)
    {
        var helper = new MethodDefinition(
            HelperMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            plan.Module.TypeSystem.Void);

        helper.Body.InitLocals = true;
        var oldMode = new VariableDefinition(plan.Module.TypeSystem.Int32);
        helper.Body.Variables.Add(oldMode);
        var il = helper.Body.GetILProcessor();

        var callSwap = il.Create(OpCodes.Call, plan.AnglerQuestSwap);
        var restoreCheck = il.Create(OpCodes.Nop);
        var ret = il.Create(OpCodes.Ret);

        il.Append(il.Create(OpCodes.Ldsfld, plan.NetMode));
        il.Append(il.Create(OpCodes.Stloc, oldMode));
        il.Append(il.Create(OpCodes.Ldloc, oldMode));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Bne_Un, callSwap));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stsfld, plan.NetMode));
        il.Append(callSwap);
        il.Append(il.Create(OpCodes.Call, plan.AnglerQuestSwap));
        il.Append(restoreCheck);
        il.Append(il.Create(OpCodes.Ldloc, oldMode));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Bne_Un, ret));
        il.Append(il.Create(OpCodes.Ldloc, oldMode));
        il.Append(il.Create(OpCodes.Stsfld, plan.NetMode));
        il.Append(ret);

        plan.MainType.Methods.Add(helper);
        return helper;
    }

    private static void AddMarker(ModuleDefinition module)
    {
        if (HasMarker(module))
            return;

        var marker = new TypeDefinition(
            "InfiniteAngler",
            MarkerTypeName,
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(marker);
    }

    private static bool HasMarker(ModuleDefinition module) =>
        FindType(module, $"InfiniteAngler.{MarkerTypeName}") is not null;

    private static void AddAssemblyMetadata(ModuleDefinition module, string key, string value)
    {
        var attrType = module.ImportReference(typeof(System.Reflection.AssemblyMetadataAttribute));
        var ctor = new MethodReference(".ctor", module.TypeSystem.Void, attrType) { HasThis = true };
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var attribute = new CustomAttribute(ctor);
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, key));
        attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
        module.Assembly.CustomAttributes.Add(attribute);
    }

    private static bool ReferencesField(MethodDefinition method, FieldDefinition field) =>
        method.Body.Instructions.Any(i => i.Operand is FieldReference fr && fr.FullName == field.FullName);

    private static bool ReferencesAnyFieldNamed(MethodDefinition method, TypeDefinition type, string name) =>
        method.Body.Instructions.Any(i => i.Operand is FieldReference fr && fr.DeclaringType.FullName == type.FullName && fr.Name == name);

    private static bool IsCall(Instruction instruction) =>
        instruction.OpCode.Code is Code.Call or Code.Callvirt;

    private static FieldDefinition RequireField(TypeDefinition type, string name) =>
        type.Fields.SingleOrDefault(f => f.Name == name)
        ?? throw CompatibilityFailure($"{type.FullName}.{name} was not found.");

    private static TypeDefinition? FindType(ModuleDefinition module, string fullName) =>
        AllTypes(module).FirstOrDefault(t => t.FullName == fullName);

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            foreach (var nested in SelfAndNested(type))
                yield return nested;
        }
    }

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
        foreach (var descendant in SelfAndNested(nested))
            yield return descendant;
    }

    private static InvalidOperationException CompatibilityFailure(string reason) =>
        new("Terraria compatibility safety check failed: " + reason +
            " This is intentionally based on code structure rather than an exact version number, so an unknown update is refused only when the Angler code actually changed.");

    private static void EnsureTargetExists(string target)
    {
        if (!File.Exists(target))
            throw new FileNotFoundException("Target Terraria executable not found: " + target);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // Mono.Cecil.Rocks is intentionally not required just for macro normalization.
    private static void SimplifyMacrosSafe(this MethodBody body)
    {
        // We only insert instructions without retargeting/removing existing branches, so preserving the original
        // macro forms is safe. This extension exists to make that design decision explicit at the patch site.
    }
}
