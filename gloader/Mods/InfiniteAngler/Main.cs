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

// Vanilla resets the Angler round at dawn by clearing the completion list and
// calling AnglerQuestSwap(). Remove exactly those two operations; everything else
// about the day transition stays vanilla.
[HarmonyPatch]
internal static class InfiniteAnglerDawnPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.UpdateTimeMethod;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var swap = InfiniteAnglerRuntime.AnglerQuestSwapMethod;
        var finishedToday = InfiniteAnglerRuntime.FinishedTodayField;

        var swapIndexes = list
            .Select((instruction, index) => new { instruction, index })
            .Where(entry => entry.instruction.Calls(swap))
            .Select(entry => entry.index)
            .ToArray();

        if (swapIndexes.Length != 1)
            throw new InvalidOperationException(
                "Expected exactly one Main.AnglerQuestSwap() call in Main.UpdateTime(), found " +
                swapIndexes.Length + ".");

        var swapIndex = swapIndexes[0];
        var clearIndex = FindDawnCompletionClear(list, swapIndex, finishedToday);

        // The Clear() instance is already on the stack, so Pop replaces the void
        // call without disturbing IL stack balance. AnglerQuestSwap() is static/no-arg.
        list[clearIndex].opcode = OpCodes.Pop;
        list[clearIndex].operand = null;
        list[swapIndex].opcode = OpCodes.Nop;
        list[swapIndex].operand = null;

        return list;
    }

    private static int FindDawnCompletionClear(
        IList<CodeInstruction> instructions,
        int swapIndex,
        FieldInfo finishedToday)
    {
        var start = Math.Max(0, swapIndex - 32);
        var end = Math.Min(instructions.Count - 1, swapIndex + 32);

        for (var index = start; index <= end; index++)
        {
            if (!(instructions[index].operand is MethodInfo method) ||
                method.Name != "Clear" ||
                method.ReturnType != typeof(void) ||
                method.GetParameters().Length != 0)
            {
                continue;
            }

            for (var previous = index - 1; previous >= Math.Max(start, index - 4); previous--)
            {
                if (ReferenceEquals(instructions[previous].operand, finishedToday) ||
                    Equals(instructions[previous].operand, finishedToday))
                {
                    return index;
                }
            }
        }

        throw new InvalidOperationException(
            "Could not find vanilla's anglerWhoFinishedToday.Clear() near AnglerQuestSwap().");
    }
}

// The server checks the shared round after normal vanilla time processing. This is
// also what makes a disconnected player stop blocking the round immediately.
[HarmonyPatch]
internal static class InfiniteAnglerRoundPatch
{
    private static MethodBase TargetMethod() => InfiniteAnglerRuntime.UpdateTimeMethod;

    [HarmonyPostfix]
    private static void Postfix() => InfiniteAnglerRuntime.TryAdvanceQuest();
}

internal static class InfiniteAnglerRuntime
{
    private static FieldInfo _netMode;
    private static FieldInfo _players;
    private static bool _advancing;
    private static bool _advanceFailureLogged;

    public static FieldInfo FinishedTodayField { get; private set; }
    public static MethodBase UpdateTimeMethod { get; private set; }
    public static MethodInfo AnglerQuestSwapMethod { get; private set; }

    public static void Initialize()
    {
        _netMode = RequireField(typeof(Main), "netMode", typeof(int));
        FinishedTodayField = RequireField(typeof(Main), "anglerWhoFinishedToday", null);
        _players = RequireField(typeof(Main), "player", null);

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

        if (!typeof(IList<string>).IsAssignableFrom(FinishedTodayField.FieldType))
            throw new InvalidOperationException(
                "Main.anglerWhoFinishedToday is no longer an IList<string>.");

        if (!_players.FieldType.IsArray)
            throw new InvalidOperationException("Main.player is no longer an array.");
    }

    public static void TryAdvanceQuest()
    {
        if (_advancing || GetNetMode() != 2)
            return;

        var finishedToday = GetFinishedToday();
        if (!AllConnectedPlayersFinished(finishedToday))
            return;

        var completedNames = finishedToday.ToArray();

        try
        {
            _advancing = true;

            // Vanilla normally clears this list at dawn, outside AnglerQuestSwap().
            // Our round ends here instead, so clear it immediately before asking
            // vanilla to choose and broadcast the next global quest.
            finishedToday.Clear();
            AnglerQuestSwapMethod.Invoke(null, null);
            _advanceFailureLogged = false;
        }
        catch (Exception ex)
        {
            // Keep the completed round intact if the vanilla swap fails so players
            // are not accidentally made eligible to claim the same quest again.
            finishedToday.Clear();
            foreach (var name in completedNames)
                finishedToday.Add(name);

            if (!_advanceFailureLogged)
            {
                _advanceFailureLogged = true;
                Console.Error.WriteLine("[Infinite Angler] Quest advance failed: " + Unwrap(ex));
            }
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
        var field = AccessTools.Field(player.GetType(), "active")
                    ?? throw new MissingFieldException(player.GetType().FullName, "active");

        if (field.FieldType != typeof(bool))
            throw new InvalidOperationException(
                player.GetType().FullName + ".active is no longer bool.");

        return (bool)field.GetValue(player);
    }

    private static string GetPlayerName(object player)
    {
        var field = AccessTools.Field(player.GetType(), "name")
                    ?? throw new MissingFieldException(player.GetType().FullName, "name");

        if (field.FieldType != typeof(string))
            throw new InvalidOperationException(
                player.GetType().FullName + ".name is no longer string.");

        return field.GetValue(player) as string;
    }

    private static IList<string> GetFinishedToday()
        => (IList<string>)FinishedTodayField.GetValue(null);

    private static int GetNetMode()
        => (int)_netMode.GetValue(null);

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
    public static void Load() { }
}
#endif
