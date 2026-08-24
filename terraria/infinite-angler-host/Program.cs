using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InfiniteAnglerHost;

internal static class Program
{
    private const string Marker = "InfiniteAnglerHost.__InfiniteAnglerHostPatchMarker";
    private const string PersonalMarker = "InfiniteAngler.__InfiniteAnglerPatchMarker";
    private const string ManifestName = "InfiniteAnglerHost.manifest.json";

    private sealed record Manifest(
        string TargetFile,
        string BackupFile,
        string OriginalSha256,
        string PatchedSha256,
        string AssemblyVersion,
        DateTimeOffset PatchedAtUtc);

    private sealed record Plan(
        AssemblyDefinition Assembly,
        ModuleDefinition Module,
        TypeDefinition MainType,
        MethodDefinition CompletionHandler,
        Instruction CompletionAddCall,
        MethodDefinition AnglerQuestSwap,
        MethodDefinition SendData,
        MethodDefinition FromLiteral,
        FieldDefinition NetMode,
        FieldDefinition AnglerQuest,
        FieldDefinition AnglerQuestFinished,
        FieldDefinition FinishedToday,
        FieldDefinition PlayerArray,
        FieldDefinition PlayerName,
        FieldDefinition WhoAmI,
        int AnglerQuestMessageId,
        int AnglerQuestFinishedMessageId,
        int QuestsCountSyncMessageId,
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
            string command = args.Any(x => x.Equals("--restore", StringComparison.OrdinalIgnoreCase))
                ? "restore"
                : args.Any(x => x.Equals("--check", StringComparison.OrdinalIgnoreCase))
                    ? "check"
                    : "install";

            string target = ResolveTarget(args);
            string manifestPath = Path.Combine(Path.GetDirectoryName(target)!, ManifestName);

            Console.WriteLine("Infinite Angler Host - Option A (host only)");
            Console.WriteLine("------------------------------------------------");
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
            Console.Error.WriteLine("No intentional replacement of the target executable was performed after this failure.");
            return 1;
        }
    }

    private static string ResolveTarget(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--target", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[i + 1]);
        }

        string cwdServer = Path.Combine(Environment.CurrentDirectory, "TerrariaServer.exe");
        if (File.Exists(cwdServer))
            return Path.GetFullPath(cwdServer);

        string besideServer = Path.Combine(AppContext.BaseDirectory, "TerrariaServer.exe");
        if (File.Exists(besideServer))
            return Path.GetFullPath(besideServer);

        bool clientPresent = File.Exists(Path.Combine(Environment.CurrentDirectory, "Terraria.exe")) ||
                             File.Exists(Path.Combine(AppContext.BaseDirectory, "Terraria.exe"));
        if (clientPresent)
        {
            throw new FileNotFoundException(
                "Terraria.exe was found, but Option A patches the Host & Play server executable, TerrariaServer.exe. " +
                "Put InfiniteAnglerHost.exe in the Terraria install folder containing TerrariaServer.exe, " +
                "or use --target \"C:\\...\\TerrariaServer.exe\".");
        }

        throw new FileNotFoundException(
            "TerrariaServer.exe was not found. Put InfiniteAnglerHost.exe in the Terraria install folder, " +
            "or use --target \"C:\\...\\TerrariaServer.exe\".");
    }

    private static int Check(string target)
    {
        EnsureTarget(target);
        using var p = BuildPlan(target);

        Console.WriteLine($"Assembly: {p.Assembly.Name.Version}");
        Console.WriteLine($"Completion handler: {p.CompletionHandler.FullName}");
        Console.WriteLine($"Packet sender: {p.SendData.FullName}");
        Console.WriteLine($"Message IDs: quest={p.AnglerQuestMessageId}, finished={p.AnglerQuestFinishedMessageId}, count={p.QuestsCountSyncMessageId}");
        Console.WriteLine(HasType(p.Module, Marker)
            ? "Status: host patch already installed."
            : "Status: compatible server-side structural match found.");
        Console.WriteLine("Compatibility is structural; it is not hard-locked to a Terraria version string or packet number.");
        return 0;
    }

    private static int Install(string target, string manifestPath)
    {
        EnsureTarget(target);
        using var p = BuildPlan(target);

        if (HasType(p.Module, PersonalMarker))
        {
            throw new InvalidOperationException(
                "The personal/Option B marker is present in this target. Refusing to stack the two patch styles into one assembly.");
        }

        if (HasType(p.Module, Marker))
        {
            Console.WriteLine("Host patch is already installed. No changes made.");
            return 0;
        }

        string root = Path.GetDirectoryName(target)!;
        string originalHash = Hash(target);
        string backupDir = Path.Combine(root, "InfiniteAnglerHost-backups");
        Directory.CreateDirectory(backupDir);

        string targetStem = Path.GetFileNameWithoutExtension(target);
        string targetExt = Path.GetExtension(target);
        string backup = Path.Combine(backupDir, $"{targetStem}.{originalHash[..12]}.original{targetExt}");
        if (!File.Exists(backup))
            File.Copy(target, backup, overwrite: false);

        Apply(p);

        string tmp = target + ".InfiniteAnglerHost.tmp";
        try
        {
            p.Assembly.Write(tmp, new WriterParameters { WriteSymbols = false });
            using (var verify = AssemblyDefinition.ReadAssembly(tmp, new ReaderParameters
                   {
                       InMemory = true,
                       ReadingMode = ReadingMode.Deferred,
                       AssemblyResolver = p.Resolver
                   }))
            {
                if (!HasType(verify.MainModule, Marker))
                    throw new InvalidOperationException("Patched-file verification failed: host patch marker missing.");
            }

            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }

        string patchedHash = Hash(target);
        var manifest = new Manifest(
            Path.GetFileName(target),
            Path.GetRelativePath(root, backup),
            originalHash,
            patchedHash,
            p.Assembly.Name.Version?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow);

        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine("PATCHED.");
        Console.WriteLine($"Original backup: {backup}");
        Console.WriteLine("Only the Host & Play PC needs this patch. Joining clients stay completely vanilla.");
        Console.WriteLine("Launch Terraria.exe normally through Steam and use Multiplayer -> Host & Play.");
        return 0;
    }

    private static int Restore(string target, string manifestPath)
    {
        EnsureTarget(target);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("InfiniteAnglerHost.manifest.json was not found beside the server executable.");

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("The Infinite Angler Host manifest could not be read.");

        if (!Path.GetFileName(target).Equals(manifest.TargetFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The manifest belongs to a different target executable. Refusing to restore it here.");

        if (!Hash(target).Equals(manifest.PatchedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The server executable no longer matches the file Infinite Angler Host patched. Steam may have updated it. " +
                "Refusing to restore an older executable over a newer game version.");
        }

        string root = Path.GetDirectoryName(target)!;
        string backup = Path.GetFullPath(Path.Combine(root, manifest.BackupFile));
        if (!File.Exists(backup))
            throw new FileNotFoundException("The matching original backup is missing: " + backup);
        if (!Hash(backup).Equals(manifest.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original backup hash does not match the manifest.");

        File.Copy(backup, target, overwrite: true);
        File.Delete(manifestPath);
        Console.WriteLine($"RESTORED vanilla {Path.GetFileName(target)} from the matching backup.");
        return 0;
    }

    private static Plan BuildPlan(string target)
    {
        var resolver = Resolver(target);
        AssemblyDefinition asm;
        try
        {
            asm = AssemblyDefinition.ReadAssembly(target, new ReaderParameters
            {
                InMemory = true,
                ReadingMode = ReadingMode.Deferred,
                AssemblyResolver = resolver
            });
        }
        catch (BadImageFormatException ex)
        {
            resolver.Dispose();
            throw new InvalidOperationException(
                "The target is not a managed Terraria server assembly Mono.Cecil can patch. No changes were made.", ex);
        }
        catch
        {
            resolver.Dispose();
            throw;
        }

        try
        {
            var mod = asm.MainModule;
            var main = Type(mod, "Terraria.Main") ?? Fail<TypeDefinition>("Terraria.Main missing");
            var messageBuffer = Type(mod, "Terraria.MessageBuffer") ?? Fail<TypeDefinition>("Terraria.MessageBuffer missing");
            var player = Type(mod, "Terraria.Player") ?? Fail<TypeDefinition>("Terraria.Player missing");
            var netMessage = Type(mod, "Terraria.NetMessage") ?? Fail<TypeDefinition>("Terraria.NetMessage missing");
            var networkText = Type(mod, "Terraria.Localization.NetworkText") ?? Fail<TypeDefinition>("Terraria.Localization.NetworkText missing");
            var messageId = Type(mod, "Terraria.ID.MessageID") ?? Fail<TypeDefinition>("Terraria.ID.MessageID missing");

            var netMode = Field(main, "netMode");
            var quest = Field(main, "anglerQuest");
            var questFinished = Field(main, "anglerQuestFinished");
            var finishedToday = Field(main, "anglerWhoFinishedToday");
            var players = Field(main, "player");
            var playerName = Field(player, "name");
            var whoAmI = Field(messageBuffer, "whoAmI");

            if (netMode.FieldType.MetadataType != MetadataType.Int32 ||
                quest.FieldType.MetadataType != MetadataType.Int32 ||
                questFinished.FieldType.MetadataType != MetadataType.Boolean ||
                whoAmI.FieldType.MetadataType != MetadataType.Int32 ||
                playerName.FieldType.MetadataType != MetadataType.String)
            {
                Fail<object>("core Angler/network field types changed");
            }

            int questMessageId = MessageId(messageId, "AnglerQuest");
            int finishedMessageId = MessageId(messageId, "AnglerQuestFinished");
            int countMessageId = MessageId(messageId, "QuestsCountSync");
            if (questMessageId == finishedMessageId || questMessageId == countMessageId || finishedMessageId == countMessageId)
                Fail<object>("Angler message IDs are no longer distinct");

            var swap = main.Methods.SingleOrDefault(x =>
                           x.Name == "AnglerQuestSwap" && x.IsStatic && !x.HasParameters &&
                           x.ReturnType.MetadataType == MetadataType.Void)
                       ?? Fail<MethodDefinition>("Main.AnglerQuestSwap() missing");

            if (Refs(swap, finishedToday))
            {
                Fail<object>(
                    "Main.AnglerQuestSwap() now directly touches anglerWhoFinishedToday; refusing to use it as a private reroll scratch operation");
            }

            var allowedSwapStores = new HashSet<string>(StringComparer.Ordinal)
            {
                quest.FullName,
                questFinished.FullName
            };
            var unexpectedStores = swap.Body.Instructions
                .Where(i => i.OpCode.Code == Code.Stsfld && i.Operand is FieldReference)
                .Select(i => (FieldReference)i.Operand)
                .Where(f => f.DeclaringType.FullName == main.FullName && !allowedSwapStores.Contains(f.FullName))
                .Select(f => f.FullName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (unexpectedStores.Length != 0)
            {
                Fail<object>(
                    "Main.AnglerQuestSwap() now writes additional Main state: " + string.Join(", ", unexpectedStores));
            }

            var fromLiteral = networkText.Methods.SingleOrDefault(x =>
                                  x.Name == "FromLiteral" && x.IsStatic && x.Parameters.Count == 1 &&
                                  x.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
                                  x.ReturnType.FullName == networkText.FullName)
                              ?? Fail<MethodDefinition>("NetworkText.FromLiteral(string) missing");

            var sends = netMessage.Methods.Where(x =>
                    x.Name == "SendData" && x.IsStatic && x.ReturnType.MetadataType == MetadataType.Void &&
                    x.Parameters.Count >= 4 &&
                    x.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
                    x.Parameters[1].ParameterType.MetadataType == MetadataType.Int32 &&
                    x.Parameters[2].ParameterType.MetadataType == MetadataType.Int32 &&
                    x.Parameters[3].ParameterType.FullName == networkText.FullName)
                .ToArray();
            if (sends.Length != 1)
                Fail<object>($"expected one vanilla NetMessage.SendData overload, found {sends.Length}");
            var sendData = sends[0];

            if (!Refs(sendData, quest) || !Refs(sendData, finishedToday))
            {
                Fail<object>(
                    "NetMessage.SendData no longer contains the expected Angler quest + per-name completion serialization dependencies");
            }

            for (int i = 4; i < sendData.Parameters.Count; i++)
            {
                if (sendData.Parameters[i].ParameterType.MetadataType is not (MetadataType.Int32 or MetadataType.Single))
                    Fail<object>($"unsupported SendData parameter type {sendData.Parameters[i].ParameterType.FullName}");
            }

            var candidates = new List<(MethodDefinition Method, Instruction Add, int Score)>();
            foreach (var method in messageBuffer.Methods.Where(x => x.HasBody))
            {
                var instructions = method.Body.Instructions;
                for (int i = 0; i < instructions.Count; i++)
                {
                    if (!IsListAdd(instructions[i]) || !Nearby(instructions, i, finishedToday, 10))
                        continue;

                    int score = 0;
                    if (Refs(method, netMode)) score += 2;
                    if (Refs(method, whoAmI)) score += 4;
                    if (Refs(method, players)) score += 2;
                    if (Refs(method, playerName)) score += 2;
                    if (ContainsIntegerConstant(method, finishedMessageId)) score += 2;
                    candidates.Add((method, instructions[i], score));
                }
            }

            if (candidates.Count == 0)
                Fail<object>("server Angler completion-name Add path not found");

            int bestScore = candidates.Max(x => x.Score);
            var best = candidates.Where(x => x.Score == bestScore).ToArray();
            if (bestScore < 6 || best.Length != 1)
            {
                string detail = string.Join("; ", candidates.Select(x => $"{x.Method.FullName} [score {x.Score}]"));
                Fail<object>("server Angler completion path was not uniquely identifiable: " + detail);
            }

            return new Plan(
                asm, mod, main,
                best[0].Method, best[0].Add,
                swap, sendData, fromLiteral,
                netMode, quest, questFinished, finishedToday,
                players, playerName, whoAmI,
                questMessageId, finishedMessageId, countMessageId,
                resolver);
        }
        catch
        {
            asm.Dispose();
            resolver.Dispose();
            throw;
        }
    }

    private static void Apply(Plan p)
    {
        p.Module.Types.Add(new TypeDefinition(
            "InfiniteAnglerHost",
            "__InfiniteAnglerHostPatchMarker",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            p.Module.TypeSystem.Object));

        var helper = Helper(p);
        var il = p.CompletionHandler.Body.GetILProcessor();
        var after = p.CompletionAddCall.Next;
        var loadThis = il.Create(OpCodes.Ldarg_0);
        var loadWho = il.Create(OpCodes.Ldfld, p.WhoAmI);
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

    private static MethodDefinition Helper(Plan p)
    {
        var h = new MethodDefinition(
            "__InfiniteAnglerHost_OnQuestCompleted",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            p.Module.TypeSystem.Void);
        h.Parameters.Add(new ParameterDefinition("whoAmI", ParameterAttributes.None, p.Module.TypeSystem.Int32));
        h.Body.InitLocals = true;

        var oldQuest = new VariableDefinition(p.Module.TypeSystem.Int32);
        var oldMode = new VariableDefinition(p.Module.TypeSystem.Int32);
        var oldQuestFinished = new VariableDefinition(p.Module.TypeSystem.Boolean);
        var name = new VariableDefinition(p.Module.TypeSystem.String);
        h.Body.Variables.Add(oldQuest);
        h.Body.Variables.Add(oldMode);
        h.Body.Variables.Add(oldQuestFinished);
        h.Body.Variables.Add(name);

        var add = (MethodReference)p.CompletionAddCall.Operand;
        var remove = new MethodReference("Remove", p.Module.TypeSystem.Boolean, add.DeclaringType)
        {
            HasThis = add.HasThis,
            ExplicitThis = add.ExplicitThis,
            CallingConvention = add.CallingConvention
        };
        remove.Parameters.Add(new ParameterDefinition(add.Parameters[0].ParameterType));

        var il = h.Body.GetILProcessor();
        var ret = il.Create(OpCodes.Ret);

        // This helper belongs only on the actual server-side completion path.
        il.Append(il.Create(OpCodes.Ldsfld, p.NetMode));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Bne_Un, ret));

        // Resolve the completing character's vanilla player name.
        il.Append(il.Create(OpCodes.Ldsfld, p.PlayerArray));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldelem_Ref));
        il.Append(il.Create(OpCodes.Ldfld, p.PlayerName));
        il.Append(il.Create(OpCodes.Stloc, name));

        // Undo only the daily completion entry that vanilla just added for this player.
        il.Append(il.Create(OpCodes.Ldsfld, p.FinishedToday));
        il.Append(il.Create(OpCodes.Ldloc, name));
        il.Append(il.Create(OpCodes.Callvirt, remove));
        il.Append(il.Create(OpCodes.Pop));

        // Snapshot shared world state that AnglerQuestSwap is allowed to change.
        il.Append(il.Create(OpCodes.Ldsfld, p.AnglerQuest));
        il.Append(il.Create(OpCodes.Stloc, oldQuest));
        il.Append(il.Create(OpCodes.Ldsfld, p.AnglerQuestFinished));
        il.Append(il.Create(OpCodes.Stloc, oldQuestFinished));
        il.Append(il.Create(OpCodes.Ldsfld, p.NetMode));
        il.Append(il.Create(OpCodes.Stloc, oldMode));

        // Borrow vanilla quest selection without allowing its normal network broadcast.
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stsfld, p.NetMode));
        il.Append(il.Create(OpCodes.Call, p.AnglerQuestSwap));
        il.Append(il.Create(OpCodes.Ldloc, oldMode));
        il.Append(il.Create(OpCodes.Stsfld, p.NetMode));

        // Send the newly-selected quest only to the player who completed the previous one.
        il.Append(il.Create(OpCodes.Ldc_I4, p.AnglerQuestMessageId));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_M1));
        il.Append(il.Create(OpCodes.Ldloc, name));
        il.Append(il.Create(OpCodes.Call, p.FromLiteral));
        for (int i = 4; i < p.SendData.Parameters.Count; i++)
        {
            if (p.SendData.Parameters[i].ParameterType.MetadataType == MetadataType.Int32)
                il.Append(il.Create(OpCodes.Ldc_I4_0));
            else
                il.Append(il.Create(OpCodes.Ldc_R4, 0f));
        }
        il.Append(il.Create(OpCodes.Call, p.SendData));

        // Put the server's shared Angler state back exactly as it was before the private reroll.
        il.Append(il.Create(OpCodes.Ldloc, oldQuest));
        il.Append(il.Create(OpCodes.Stsfld, p.AnglerQuest));
        il.Append(il.Create(OpCodes.Ldloc, oldQuestFinished));
        il.Append(il.Create(OpCodes.Stsfld, p.AnglerQuestFinished));
        il.Append(ret);

        p.MainType.Methods.Add(h);
        return h;
    }

    private static int MessageId(TypeDefinition messageIdType, string name)
    {
        var field = messageIdType.Fields.SingleOrDefault(f => f.Name == name)
                    ?? Fail<FieldDefinition>($"Terraria.ID.MessageID.{name} missing");
        if (!field.IsStatic || !field.HasConstant)
            Fail<object>($"Terraria.ID.MessageID.{name} is no longer a static constant");

        int value;
        try
        {
            value = Convert.ToInt32(field.Constant);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Terraria compatibility check failed: could not read MessageID.{name}. No changes were made.", ex);
        }

        if (value < 0 || value > byte.MaxValue)
            Fail<object>($"Terraria.ID.MessageID.{name} is outside the vanilla byte packet range");
        return value;
    }

    private static bool ContainsIntegerConstant(MethodDefinition method, int value)
    {
        if (!method.HasBody)
            return false;

        return method.Body.Instructions.Any(i => i.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => value == -1,
            Code.Ldc_I4_0 => value == 0,
            Code.Ldc_I4_1 => value == 1,
            Code.Ldc_I4_2 => value == 2,
            Code.Ldc_I4_3 => value == 3,
            Code.Ldc_I4_4 => value == 4,
            Code.Ldc_I4_5 => value == 5,
            Code.Ldc_I4_6 => value == 6,
            Code.Ldc_I4_7 => value == 7,
            Code.Ldc_I4_8 => value == 8,
            Code.Ldc_I4_S => Convert.ToInt32(i.Operand) == value,
            Code.Ldc_I4 => Convert.ToInt32(i.Operand) == value,
            _ => false
        });
    }

    private static bool IsListAdd(Instruction i)
    {
        if (i.OpCode.Code is not (Code.Call or Code.Callvirt) ||
            i.Operand is not MethodReference m ||
            m.Name != "Add" ||
            m.Parameters.Count != 1)
            return false;

        return m.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal);
    }

    private static bool Nearby(Mono.Collections.Generic.Collection<Instruction> instructions, int at, FieldDefinition field, int radius)
    {
        for (int i = Math.Max(0, at - radius); i <= at; i++)
        {
            if (instructions[i].Operand is FieldReference fr && fr.FullName == field.FullName)
                return true;
        }
        return false;
    }

    private static bool Refs(MethodDefinition method, FieldDefinition field) =>
        method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is FieldReference fr && fr.FullName == field.FullName);

    private static FieldDefinition Field(TypeDefinition type, string name) =>
        type.Fields.SingleOrDefault(x => x.Name == name)
        ?? Fail<FieldDefinition>($"{type.FullName}.{name} missing");

    private static TypeDefinition? Type(ModuleDefinition module, string fullName) =>
        Types(module).FirstOrDefault(x => x.FullName == fullName);

    private static IEnumerable<TypeDefinition> Types(ModuleDefinition module)
    {
        foreach (var type in module.Types)
            foreach (var nested in Nested(type))
                yield return nested;
    }

    private static IEnumerable<TypeDefinition> Nested(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
            foreach (var inner in Nested(nested))
                yield return inner;
    }

    private static bool HasType(ModuleDefinition module, string fullName) => Type(module, fullName) is not null;

    private static T Fail<T>(string reason) =>
        throw new InvalidOperationException("Terraria compatibility check failed: " + reason + ". No changes were made.");

    private static DefaultAssemblyResolver Resolver(string target)
    {
        var resolver = new DefaultAssemblyResolver();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;
            directory = Path.GetFullPath(directory);
            if (seen.Add(directory))
                resolver.AddSearchDirectory(directory);
        }

        Add(Path.GetDirectoryName(target));
        Add(AppContext.BaseDirectory);
        try
        {
            Add(RuntimeEnvironment.GetRuntimeDirectory());
        }
        catch
        {
            // Target-directory resolution is still available.
        }

        return resolver;
    }

    private static void EnsureTarget(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Target file not found.", path);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
