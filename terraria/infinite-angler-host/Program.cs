using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InfiniteAnglerHost;

internal static class Program
{
    private const string MarkerFullName = "InfiniteAnglerHost.__InfiniteAnglerHostPatchMarker";
    private const string PersonalMarkerFullName = "InfiniteAngler.__InfiniteAnglerPatchMarker";
    private const string HelperMethodName = "__InfiniteAnglerHost_OnQuestCompleted";
    private const string ManifestName = "InfiniteAnglerHost.manifest.json";

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
        TypeDefinition MessageBufferType,
        TypeDefinition PlayerType,
        MethodDefinition CompletionHandler,
        Instruction CompletionAddCall,
        MethodDefinition AnglerQuestSwap,
        MethodDefinition SendData,
        MethodDefinition NetworkTextFromLiteral,
        FieldDefinition NetMode,
        FieldDefinition AnglerQuest,
        FieldDefinition AnglerWhoFinishedToday,
        FieldDefinition PlayerArray,
        FieldDefinition PlayerName,
        FieldDefinition BufferWhoAmI,
        DefaultAssemblyResolver Resolver) : IDisposable
    {
        public void Dispose()
        {
            Assembly.Dispose();
            Resolver.Dispose();
        }
    }

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
            var root = Path.GetDirectoryName(target)!;
            var manifestPath = Path.Combine(root, ManifestName);

            Console.WriteLine("Infinite Angler Host - host-only vanilla Terraria patcher");
            Console.WriteLine("--------------------------------------------------------");
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
            if (Environment.GetEnvironmentVariable("CI") is not null)
                Console.Error.WriteLine(ex);
            Console.Error.WriteLine("No intentional replacement of Terraria.exe was performed after this failure.");
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

        var beside = Path.Combine(AppContext.BaseDirectory, "Terraria.exe");
        if (File.Exists(beside))
            return Path.GetFullPath(beside);

        throw new FileNotFoundException(
            "Terraria.exe was not found. Put InfiniteAnglerHost.exe beside Terraria.exe, " +
            "or use --target \"C:\\...\\Terraria.exe\".");
    }

    private static int Check(string target)
    {
        EnsureTargetExists(target);
        using var plan = BuildPlan(target);

        Console.WriteLine($"Managed assembly version: {AssemblyVersion(plan.Assembly)}");
        Console.WriteLine($"Completion handler: {plan.CompletionHandler.FullName}");
        Console.WriteLine($"Quest packet sender: {plan.SendData.FullName}");
        Console.WriteLine(HasMarker(plan.Module, MarkerFullName)
            ? "Status: host patch already installed."
            : "Status: compatible host-side structural match found.");
        Console.WriteLine("Compatibility is structural, not locked to a Terraria version string.");
        return 0;
    }

    private static int Install(string target, string manifestPath)
    {
        EnsureTargetExists(target);
        using var plan = BuildPlan(target);

        if (HasMarker(plan.Module, PersonalMarkerFullName))
        {
            throw new InvalidOperationException(
                "The Personal/Option B Infinite Angler patch is already installed in this Terraria.exe. " +
                "Restore Option B first, then install the Host/Option A patch so backup chains stay unambiguous.");
        }

        if (HasMarker(plan.Module, MarkerFullName))
        {
            Console.WriteLine("Host patch is already installed. No changes made.");
            return 0;
        }

        var version = AssemblyVersion(plan.Assembly);
        Console.WriteLine($"Managed assembly version: {version}");
        Console.WriteLine($"Validated completion handler: {plan.CompletionHandler.FullName}");
        Console.WriteLine("Safety check: server completion + vanilla quest packet structure matched.");
        Console.WriteLine("Applying host-only endless Angler quests for vanilla guests.");

        var originalHash = Sha256(target);
        var root = Path.GetDirectoryName(target)!;
        var backupDir = Path.Combine(root, "InfiniteAnglerHost-backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, $"Terraria.{originalHash[..12]}.original.exe");
        if (!File.Exists(backupPath))
            File.Copy(target, backupPath, overwrite: false);

        ApplyPatch(plan);

        var tempPath = target + ".InfiniteAnglerHost.tmp";
        try
        {
            plan.Assembly.Write(tempPath, new WriterParameters { WriteSymbols = false });
            using (var verification = AssemblyDefinition.ReadAssembly(tempPath, new ReaderParameters
                   {
                       ReadSymbols = false,
                       InMemory = true,
                       ReadingMode = ReadingMode.Deferred,
                       AssemblyResolver = plan.Resolver
                   }))
            {
                if (!HasMarker(verification.MainModule, MarkerFullName))
                    throw new InvalidOperationException("Patched-file verification failed: host patch marker missing.");
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
            Path.GetRelativePath(root, backupPath),
            originalHash,
            patchedHash,
            version,
            DateTimeOffset.UtcNow);
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine("PATCHED.");
        Console.WriteLine($"Original backup: {backupPath}");
        Console.WriteLine("Only the Host & Play PC needs this patch. Joining players remain vanilla.");
        Console.WriteLine("Launch Terraria normally through Steam and use Multiplayer -> Host & Play.");
        return 0;
    }

    private static int Restore(string target, string manifestPath)
    {
        EnsureTargetExists(target);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("No InfiniteAnglerHost.manifest.json was found beside Terraria.exe.");

        var manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("The Infinite Angler Host manifest could not be read.");

        var currentHash = Sha256(target);
        if (!currentHash.Equals(manifest.PatchedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Terraria.exe no longer matches the file Infinite Angler Host patched. Steam may have updated it. " +
                "Refusing to restore an older executable over a newer game version.");
        }

        var root = Path.GetDirectoryName(target)!;
        var backupPath = Path.GetFullPath(Path.Combine(root, manifest.BackupFile));
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("The recorded original backup is missing: " + backupPath);
        if (!Sha256(backupPath).Equals(manifest.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original backup hash does not match the manifest.");

        File.Copy(backupPath, target, overwrite: true);
        File.Delete(manifestPath);
        Console.WriteLine("RESTORED vanilla Terraria.exe from the matching host-patch backup.");
        return 0;
    }

    private static PatchPlan BuildPlan(string target)
    {
        var resolver = CreateResolver(target);
        AssemblyDefinition assembly;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(target, new ReaderParameters
            {
                ReadSymbols = false,
                InMemory = true,
                ReadingMode = ReadingMode.Deferred,
                AssemblyResolver = resolver
            });
        }
        catch (BadImageFormatException ex)
        {
            resolver.Dispose();
            throw new InvalidOperationException(
                "Terraria.exe is not a managed assembly Mono.Cecil can patch. No changes were made.", ex);
        }
        catch
        {
            resolver.Dispose();
            throw;
        }

        try
        {
            var module = assembly.MainModule;
            var main = FindType(module, "Terraria.Main")
                       ?? throw CompatibilityFailure("Terraria.Main was not found.");
            var messageBuffer = FindType(module, "Terraria.MessageBuffer")
                                ?? throw CompatibilityFailure("Terraria.MessageBuffer was not found.");
            var player = FindType(module, "Terraria.Player")
                         ?? throw CompatibilityFailure("Terraria.Player was not found.");
            var netMessage = FindType(module, "Terraria.NetMessage")
                             ?? throw CompatibilityFailure("Terraria.NetMessage was not found.");
            var networkText = FindType(module, "Terraria.Localization.NetworkText")
                              ?? throw CompatibilityFailure("Terraria.Localization.NetworkText was not found.");

            var netMode = RequireField(main, "netMode");
            var anglerQuest = RequireField(main, "anglerQuest");
            var finishedToday = RequireField(main, "anglerWhoFinishedToday");
            var playerArray = RequireField(main, "player");
            var playerName = RequireField(player, "name");
            var whoAmI = RequireField(messageBuffer, "whoAmI");

            if (netMode.FieldType.MetadataType != MetadataType.Int32 ||
                anglerQuest.FieldType.MetadataType != MetadataType.Int32 ||
                whoAmI.FieldType.MetadataType != MetadataType.Int32 ||
                playerName.FieldType.MetadataType != MetadataType.String)
                throw CompatibilityFailure("Core Angler/network field types no longer match the expected vanilla shape.");

            var swap = main.Methods.SingleOrDefault(m =>
                           m.Name == "AnglerQuestSwap" && m.IsStatic && !m.HasParameters &&
                           m.ReturnType.MetadataType == MetadataType.Void)
                       ?? throw CompatibilityFailure("Main.AnglerQuestSwap() was not found with the expected signature.");

            var fromLiteral = networkText.Methods.SingleOrDefault(m =>
                                  m.Name == "FromLiteral" && m.IsStatic && m.Parameters.Count == 1 &&
                                  m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                                  m.ReturnType.FullName == networkText.FullName)
                              ?? throw CompatibilityFailure("NetworkText.FromLiteral(string) was not found.");

            var sendCandidates = netMessage.Methods.Where(m =>
                    m.Name == "SendData" && m.IsStatic && m.ReturnType.MetadataType == MetadataType.Void &&
                    m.Parameters.Count >= 4 &&
                    m.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
                    m.Parameters[1].ParameterType.MetadataType == MetadataType.Int32 &&
                    m.Parameters[2].ParameterType.MetadataType == MetadataType.Int32 &&
                    m.Parameters[3].ParameterType.FullName == networkText.FullName)
                .ToArray();
            if (sendCandidates.Length != 1)
                throw CompatibilityFailure($"Expected exactly one vanilla NetMessage.SendData overload; found {sendCandidates.Length}.");
            var sendData = sendCandidates[0];

            if (!ReferencesField(sendData, anglerQuest) || !ReferencesField(sendData, finishedToday))
                throw CompatibilityFailure("Packet serialization no longer references both Angler quest and per-name completion state.");
            ValidateSendDataDefaults(sendData);

            var candidates = new List<(MethodDefinition Method, Instruction AddCall, int Score)>();
            foreach (var method in messageBuffer.Methods.Where(m => m.HasBody))
            {
                var instructions = method.Body.Instructions;
                for (var i = 0; i < instructions.Count; i++)
                {
                    var instruction = instructions[i];
                    if (!IsListStringAdd(instruction))
                        continue;
                    if (!NearbyReferencesField(instructions, i, finishedToday, 10))
                        continue;

                    var score = 0;
                    if (ReferencesField(method, netMode)) score += 2;
                    if (ReferencesField(method, whoAmI)) score += 4;
                    if (ReferencesField(method, playerArray)) score += 2;
                    if (ReferencesField(method, playerName)) score += 2;
                    candidates.Add((method, instruction, score));
                }
            }

            if (candidates.Count == 0)
                throw CompatibilityFailure("Could not find the server path that records an Angler completion name.");
            var bestScore = candidates.Max(c => c.Score);
            var best = candidates.Where(c => c.Score == bestScore).ToArray();
            if (bestScore < 6 || best.Length != 1)
            {
                var detail = string.Join("; ", candidates.Select(c => $"{c.Method.FullName} [score {c.Score}]"));
                throw CompatibilityFailure("Angler completion handler was not uniquely identifiable: " + detail);
            }

            return new PatchPlan(
                assembly, module, main, messageBuffer, player,
                best[0].Method, best[0].AddCall, swap, sendData, fromLiteral,
                netMode, anglerQuest, finishedToday, playerArray, playerName, whoAmI, resolver);
        }
        catch
        {
            assembly.Dispose();
            resolver.Dispose();
            throw;
        }
    }

    private static void ApplyPatch(PatchPlan plan)
    {
        AddMarker(plan.Module);
        var helper = AddHostCompletionHelper(plan);

        var il = plan.CompletionHandler.Body.GetILProcessor();
        var after = plan.CompletionAddCall.Next;
        var loadThis = il.Create(OpCodes.Ldarg_0);
        var loadWho = il.Create(OpCodes.Ldfld, plan.BufferWhoAmI);
        var call = il.Create(OpCodes.Call, helper);

        if (after is null)
        {
            il.Append(loadThis);
            il.Append(loadWho);
            il.Append(call);
        }
        else
        {
            il.InsertBefore(after, loadThis);
            il.InsertBefore(after, loadWho);
            il.InsertBefore(after, call);
        }
    }

    private static MethodDefinition AddHostCompletionHelper(PatchPlan plan)
    {
        var helper = new MethodDefinition(
            HelperMethodName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            plan.Module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition("whoAmI", ParameterAttributes.None, plan.Module.TypeSystem.Int32));
        helper.Body.InitLocals = true;

        var oldQuest = new VariableDefinition(plan.Module.TypeSystem.Int32);
        var oldMode = new VariableDefinition(plan.Module.TypeSystem.Int32);
        var playerName = new VariableDefinition(plan.Module.TypeSystem.String);
        helper.Body.Variables.Add(oldQuest);
        helper.Body.Variables.Add(oldMode);
        helper.Body.Variables.Add(playerName);

        var listType = plan.Module.ImportReference(plan.AnglerWhoFinishedToday.FieldType);
        var removeName = new MethodReference("Remove", plan.Module.TypeSystem.Boolean, listType) { HasThis = true };
        removeName.Parameters.Add(new ParameterDefinition(plan.Module.TypeSystem.String));

        var il = helper.Body.GetILProcessor();
        var ret = il.Create(OpCodes.Ret);

        // Server authority only. Host & Play's server side is netMode 2.
        il.Append(il.Create(OpCodes.Ldsfld, plan.NetMode));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Bne_Un, ret));

        // Resolve the completing player's vanilla character name.
        il.Append(il.Create(OpCodes.Ldsfld, plan.PlayerArray));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Ldfld, plan.PlayerName));
        il.Append(il.Create(OpCodes.Stloc, playerName));

        // Forget the just-added daily lockout name before serializing packet 74.
        il.Append(il.Create(OpCodes.Ldsfld, plan.AnglerWhoFinishedToday));
        il.Append(il.Create(OpCodes.Ldloc, playerName));
        il.Append(il.Create(OpCodes.Callvirt, removeName));
        il.Append(il.Create(OpCodes.Pop));

        // Ask vanilla quest selection for a valid next quest without letting AnglerQuestSwap network-broadcast it.
        il.Append(il.Create(OpCodes.Ldsfld, plan.AnglerQuest));
        il.Append(il.Create(OpCodes.Stloc, oldQuest));
        il.Append(il.Create(OpCodes.Ldsfld, plan.NetMode));
        il.Append(il.Create(OpCodes.Stloc, oldMode));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stsfld, plan.NetMode));
        il.Append(il.Create(OpCodes.Call, plan.AnglerQuestSwap));
        il.Append(il.Create(OpCodes.Ldloc, oldMode));
        il.Append(il.Create(OpCodes.Stsfld, plan.NetMode));

        // Send an ordinary vanilla Angler Quest (74) packet only to the player who completed the quest.
        EmitInt(il, 74);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_M1));
        il.Append(il.Create(OpCodes.Ldloc, playerName));
        il.Append(il.Create(OpCodes.Call, plan.NetworkTextFromLiteral));
        EmitSendDataDefaults(il, plan.SendData);
        il.Append(il.Create(OpCodes.Call, plan.SendData));

        // Restore the world's shared quest immediately; other clients never see the temporary per-player roll.
        il.Append(il.Create(OpCodes.Ldloc, oldQuest));
        il.Append(il.Create(OpCodes.Stsfld, plan.AnglerQuest));
        il.Append(ret);

        plan.MainType.Methods.Add(helper);
        return helper;
    }

    private static void ValidateSendDataDefaults(MethodDefinition sendData)
    {
        for (var i = 4; i < sendData.Parameters.Count; i++)
        {
            var type = sendData.Parameters[i].ParameterType.MetadataType;
            if (type is not (MetadataType.Int32 or MetadataType.Single))
                throw CompatibilityFailure(
                    $"NetMessage.SendData parameter {i + 1} changed to unsupported type {sendData.Parameters[i].ParameterType.FullName}.");
        }
    }

    private static void EmitSendDataDefaults(ILProcessor il, MethodDefinition sendData)
    {
        for (var i = 4; i < sendData.Parameters.Count; i++)
        {
            switch (sendData.Parameters[i].ParameterType.MetadataType)
            {
                case MetadataType.Int32:
                    il.Append(il.Create(OpCodes.Ldc_I4_0));
                    break;
                case MetadataType.Single:
                    il.Append(il.Create(OpCodes.Ldc_R4, 0f));
                    break;
                default:
                    throw CompatibilityFailure("Unexpected SendData parameter type during emission.");
            }
        }
    }

    private static void EmitInt(ILProcessor il, int value) => il.Append(il.Create(OpCodes.Ldc_I4, value));

    private static bool IsListStringAdd(Instruction instruction)
    {
        if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) || instruction.Operand is not MethodReference mr)
            return false;
        return mr.Name == "Add" && mr.Parameters.Count == 1 &&
               mr.Parameters[0].ParameterType.MetadataType == MetadataType.String;
    }

    private static bool NearbyReferencesField(Mono.Collections.Generic.Collection<Instruction> instructions, int index, FieldDefinition field, int radius)
    {
        var start = Math.Max(0, index - radius);
        for (var i = start; i <= index; i++)
        {
            if (instructions[i].Operand is FieldReference fr && fr.FullName == field.FullName)
                return true;
        }
        return false;
    }

    private static DefaultAssemblyResolver CreateResolver(string target)
    {
        var resolver = new DefaultAssemblyResolver();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            path = Path.GetFullPath(path);
            if (seen.Add(path)) resolver.AddSearchDirectory(path);
        }

        Add(Path.GetDirectoryName(target));
        Add(AppContext.BaseDirectory);
        try { Add(RuntimeEnvironment.GetRuntimeDirectory()); } catch { }
        return resolver;
    }

    private static void AddMarker(ModuleDefinition module)
    {
        if (HasMarker(module, MarkerFullName)) return;
        module.Types.Add(new TypeDefinition(
            "InfiniteAnglerHost",
            "__InfiniteAnglerHostPatchMarker",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object));
    }

    private static bool HasMarker(ModuleDefinition module, string fullName) => FindType(module, fullName) is not null;

    private static bool ReferencesField(MethodDefinition method, FieldDefinition field) =>
        method.HasBody && method.Body.Instructions.Any(i => i.Operand is FieldReference fr && fr.FullName == field.FullName);

    private static FieldDefinition RequireField(TypeDefinition type, string name) =>
        type.Fields.SingleOrDefault(f => f.Name == name)
        ?? throw CompatibilityFailure($"{type.FullName}.{name} was not found.");

    private static TypeDefinition? FindType(ModuleDefinition module, string fullName) =>
        AllTypes(module).FirstOrDefault(t => t.FullName == fullName);

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
            foreach (var nested in SelfAndNested(type))
                yield return nested;
    }

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
            foreach (var item in SelfAndNested(nested))
                yield return item;
    }

    private static Exception CompatibilityFailure(string detail) =>
        new InvalidOperationException("Terraria compatibility check failed: " + detail + " No changes were made.");

    private static void EnsureTargetExists(string target)
    {
        if (!File.Exists(target)) throw new FileNotFoundException("Target file was not found.", target);
    }

    private static string AssemblyVersion(AssemblyDefinition assembly) => assembly.Name.Version?.ToString() ?? "unknown";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
