#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

public static class Mod
{
    public static void Load()
    {
        InfiniteAnglerRuntime.Initialize();
        Console.WriteLine("[Infinite Angler] Shared endless Angler quests enabled.");
    }
}

// Vanilla's dawn transition resets the Angler quest. Remove only the Angler reset
// call; the quest now advances exclusively when all connected players finish it.
[HarmonyPatch]
internal static class InfiniteAnglerDawnPatch
{
    private static MethodBase TargetMethod()
    {
        return InfiniteAnglerRuntime.UpdateTimeMethod;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var swap = InfiniteAnglerRuntime.AnglerQuestSwapMethod;
        var removed = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(swap))
            {
                instruction.opcode = OpCodes.Nop;
                instruction.operand = null;
                removed++;
            }

            yield return instruction;
        }

        if (removed != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one Main.AnglerQuestSwap() call in Main.UpdateTime(), found " + removed + ".");
        }
    }
}

// Re-evaluate every server tick so a disconnect immediately stops counting toward
// the current round. After a quest swap the completion list is empty, so this is inert.
[HarmonyPatch]
internal static class InfiniteAnglerTickPatch
{
    private static MethodBase TargetMethod()
    {
        return InfiniteAnglerRuntime.UpdateTimeMethod;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        InfiniteAnglerRuntime.TryAdvanceQuest();
    }
}

[HarmonyPatch]
internal static class InfiniteAnglerCompletionPatch
{
    private static MethodBase TargetMethod()
    {
        return InfiniteAnglerRuntime.GetDataMethod;
    }

    [HarmonyPrefix]
    private static void Prefix(object __instance, int start, ref bool __state)
    {
        __state = InfiniteAnglerRuntime.WasAlreadyFinished(__instance, start);
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance, int start, bool __state)
    {
        InfiniteAnglerRuntime.AfterGetData(__instance, start, __state);
    }
}

internal static class InfiniteAnglerRuntime
{
    private static FieldInfo _netMode;
    private static FieldInfo _finishedToday;
    private static FieldInfo _players;
    private static FieldInfo _readBuffer;
    private static FieldInfo _whoAmI;
    private static int _anglerQuestFinishedMessageId;
    private static bool _advancing;

    public static MethodBase GetDataMethod { get; private set; }
    public static MethodBase UpdateTimeMethod { get; private set; }
    public static MethodInfo AnglerQuestSwapMethod { get; private set; }

    public static void Initialize()
    {
        _netMode = RequireField(typeof(Main), "netMode", typeof(int));
        _finishedToday = RequireField(typeof(Main), "anglerWhoFinishedToday", null);
        _players = RequireField(typeof(Main), "player", null);
        _readBuffer = RequireField(typeof(MessageBuffer), "readBuffer", typeof(byte[]));
        _whoAmI = RequireField(typeof(MessageBuffer), "whoAmI", typeof(int));

        AnglerQuestSwapMethod = typeof(Main)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == "AnglerQuestSwap" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            ?? throw new MissingMethodException(typeof(Main).FullName, "AnglerQuestSwap()");

        UpdateTimeMethod = typeof(Main)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == "UpdateTime" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            ?? throw new MissingMethodException(typeof(Main).FullName, "UpdateTime()");

        GetDataMethod = typeof(MessageBuffer)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
            {
                if (method.Name != "GetData")
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length >= 2 &&
                       parameters[0].Name == "start" &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].Name == "length" &&
                       parameters[1].ParameterType == typeof(int);
            })
            ?? throw new MissingMethodException(typeof(MessageBuffer).FullName, "GetData(int start, int length, ...)");

        var messageIdType = typeof(Main).Assembly.GetType("Terraria.ID.MessageID", throwOnError: true);
        _anglerQuestFinishedMessageId = ReadConstantInt(messageIdType, "AnglerQuestFinished");

        if (!typeof(IList<string>).IsAssignableFrom(_finishedToday.FieldType))
            throw new InvalidOperationException("Main.anglerWhoFinishedToday is no longer an IList<string>.");

        if (!_players.FieldType.IsArray)
            throw new InvalidOperationException("Main.player is no longer an array.");
    }

    public static bool WasAlreadyFinished(object messageBuffer, int start)
    {
        if (!IsCompletionPacket(messageBuffer, start) || GetNetMode() != 2)
            return true;

        if (!TryGetPlayer(messageBuffer, out _, out var name))
            return true;

        return GetFinishedToday().Contains(name);
    }

    public static void AfterGetData(object messageBuffer, int start, bool wasAlreadyFinished)
    {
        if (wasAlreadyFinished || !IsCompletionPacket(messageBuffer, start) || GetNetMode() != 2)
            return;

        if (!TryGetPlayer(messageBuffer, out _, out var name))
            return;

        var finishedToday = GetFinishedToday();
        if (!finishedToday.Contains(name))
            return;

        TryAdvanceQuest();
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        if (!AllConnectedPlayersFinished(finishedToday))
            return;

        try
        {
            _advancing = true;
            AnglerQuestSwapMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Infinite Angler] Quest advance failed: " + Unwrap(ex));
        }
        finally
        {
            _advancing = false;
        }
    }

    private static bool AllConnectedPlayersFinished(IList<string> finishedToday)
    {
        var players = (Array)_players.GetValue(null);
        if (players == null)
            return false;

        var anyConnected = false;

        for (var index = 0; index < players.Length; index++)
        {
            var player = players.GetValue(index);
            if (player == null || !IsActive(player))
                continue;

            anyConnected = true;
            var name = GetPlayerName(player);
            if (string.IsNullOrEmpty(name) || !finishedToday.Contains(name))
                return false;
        }

        return anyConnected;
    }

    private static bool IsActive(object player)
    {
        var activeField = AccessTools.Field(player.GetType(), "active")
                          ?? throw new MissingFieldException(player.GetType().FullName, "active");

        if (activeField.FieldType != typeof(bool))
            throw new InvalidOperationException(player.GetType().FullName + ".active is no longer bool.");

        return (bool)activeField.GetValue(player);
    }

    private static bool IsCompletionPacket(object messageBuffer, int start)
    {
        var buffer = (byte[])_readBuffer.GetValue(messageBuffer);
        return buffer != null &&
               start >= 0 &&
               start < buffer.Length &&
               buffer[start] == (byte)_anglerQuestFinishedMessageId;
    }

    private static bool TryGetPlayer(object messageBuffer, out object player, out string name)
    {
        player = null;
        name = null;

        var whoAmI = (int)_whoAmI.GetValue(messageBuffer);
        var players = (Array)_players.GetValue(null);
        if (players == null || whoAmI < 0 || whoAmI >= players.Length)
            return false;

        player = players.GetValue(whoAmI);
        if (player == null || !IsActive(player))
            return false;

        name = GetPlayerName(player);
        return !string.IsNullOrEmpty(name);
    }

    private static string GetPlayerName(object player)
    {
        var nameField = AccessTools.Field(player.GetType(), "name")
                        ?? throw new MissingFieldException(player.GetType().FullName, "name");

        if (nameField.FieldType != typeof(string))
            throw new InvalidOperationException(player.GetType().FullName + ".name is no longer string.");

        return nameField.GetValue(player) as string;
    }

    private static IList<string> GetFinishedToday()
    {
        return (IList<string>)_finishedToday.GetValue(null);
    }

    private static int GetNetMode()
    {
        return (int)_netMode.GetValue(null);
    }

    private static FieldInfo RequireField(Type type, string name, Type expectedType)
    {
        var field = AccessTools.Field(type, name)
                    ?? throw new MissingFieldException(type.FullName, name);

        if (expectedType != null && field.FieldType != expectedType)
        {
            throw new InvalidOperationException(
                type.FullName + "." + name + " changed type from " +
                expectedType.FullName + " to " + field.FieldType.FullName + ".");
        }

        return field;
    }

    private static int ReadConstantInt(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingFieldException(type.FullName, name);

        var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return Convert.ToInt32(value);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException invocation && invocation.InnerException != null)
            exception = invocation.InnerException;

        return exception;
    }
}
#else
public static class Mod
{
    public static void Load()
    {
    }
}
#endif
