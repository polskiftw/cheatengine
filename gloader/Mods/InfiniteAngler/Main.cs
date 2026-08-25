#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.Localization;

public static class Mod
{
    public static void Load()
    {
        InfiniteAnglerRuntime.Initialize();
        Console.WriteLine("[Infinite Angler] Server-side endless Angler quests enabled.");
    }
}

[HarmonyPatch]
internal static class InfiniteAnglerMessagePatch
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
    private static FieldInfo _anglerQuest;
    private static FieldInfo _anglerQuestFinished;
    private static FieldInfo _finishedToday;
    private static FieldInfo _players;
    private static FieldInfo _readBuffer;
    private static FieldInfo _whoAmI;
    private static MethodInfo _anglerQuestSwap;
    private static MethodInfo _sendData;
    private static MethodInfo _fromLiteral;
    private static int _anglerQuestMessageId;
    private static int _anglerQuestFinishedMessageId;

    public static MethodBase GetDataMethod { get; private set; }

    public static void Initialize()
    {
        _netMode = RequireField(typeof(Main), "netMode", typeof(int));
        _anglerQuest = RequireField(typeof(Main), "anglerQuest", typeof(int));
        _anglerQuestFinished = RequireField(typeof(Main), "anglerQuestFinished", typeof(bool));
        _finishedToday = RequireField(typeof(Main), "anglerWhoFinishedToday", null);
        _players = RequireField(typeof(Main), "player", null);
        _readBuffer = RequireField(typeof(MessageBuffer), "readBuffer", typeof(byte[]));
        _whoAmI = RequireField(typeof(MessageBuffer), "whoAmI", typeof(int));

        _anglerQuestSwap = typeof(Main)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == "AnglerQuestSwap" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 0)
            ?? throw new MissingMethodException(typeof(Main).FullName, "AnglerQuestSwap()");

        GetDataMethod = typeof(MessageBuffer)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
            {
                if (method.Name != "GetData")
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length >= 2 &&
                       parameters[0].Name == "start" &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].Name == "length" &&
                       parameters[1].ParameterType == typeof(int);
            })
            ?? throw new MissingMethodException(typeof(MessageBuffer).FullName, "GetData(int start, int length, ...)");

        var messageIdType = typeof(Main).Assembly.GetType("Terraria.ID.MessageID", throwOnError: true);
        _anglerQuestMessageId = ReadConstantInt(messageIdType, "AnglerQuest");
        _anglerQuestFinishedMessageId = ReadConstantInt(messageIdType, "AnglerQuestFinished");
        var questsCountSync = ReadConstantInt(messageIdType, "QuestsCountSync");

        if (_anglerQuestMessageId == _anglerQuestFinishedMessageId ||
            _anglerQuestMessageId == questsCountSync ||
            _anglerQuestFinishedMessageId == questsCountSync)
        {
            throw new InvalidOperationException("Angler network message IDs are no longer distinct.");
        }

        _fromLiteral = typeof(NetworkText)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "FromLiteral" &&
                       method.ReturnType == typeof(NetworkText) &&
                       parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(string);
            })
            ?? throw new MissingMethodException(typeof(NetworkText).FullName, "FromLiteral(string)");

        var netMessageType = typeof(Main).Assembly.GetType("Terraria.NetMessage", throwOnError: true);
        var sendCandidates = netMessageType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method =>
            {
                if (method.Name != "SendData" || method.ReturnType != typeof(void))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length >= 4 &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].ParameterType == typeof(int) &&
                       parameters[2].ParameterType == typeof(int) &&
                       parameters[3].ParameterType == typeof(NetworkText) &&
                       parameters.Skip(4).All(parameter =>
                           parameter.ParameterType == typeof(int) ||
                           parameter.ParameterType == typeof(float));
            })
            .ToArray();

        if (sendCandidates.Length != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one compatible Terraria.NetMessage.SendData overload, found " +
                sendCandidates.Length + ".");
        }

        _sendData = sendCandidates[0];

        if (!typeof(IList<string>).IsAssignableFrom(_finishedToday.FieldType))
        {
            throw new InvalidOperationException(
                "Main.anglerWhoFinishedToday is no longer an IList<string>.");
        }

        if (!_players.FieldType.IsArray)
        {
            throw new InvalidOperationException("Main.player is no longer an array.");
        }
    }

    public static bool WasAlreadyFinished(object messageBuffer, int start)
    {
        if (!IsCompletionPacket(messageBuffer, start) || GetNetMode() != 2)
        {
            return true;
        }

        if (!TryGetPlayerName(messageBuffer, out var name))
        {
            return true;
        }

        return GetFinishedToday().Contains(name);
    }

    public static void AfterGetData(object messageBuffer, int start, bool wasAlreadyFinished)
    {
        if (wasAlreadyFinished || !IsCompletionPacket(messageBuffer, start) || GetNetMode() != 2)
        {
            return;
        }

        if (!TryGetPlayerName(messageBuffer, out var name))
        {
            return;
        }

        var finishedToday = GetFinishedToday();
        if (!finishedToday.Contains(name))
        {
            // Vanilla did not accept this as a successful quest completion.
            return;
        }

        var whoAmI = (int)_whoAmI.GetValue(messageBuffer);
        var oldQuest = (int)_anglerQuest.GetValue(null);
        var oldQuestFinished = (bool)_anglerQuestFinished.GetValue(null);
        var oldNetMode = GetNetMode();
        var removedCompletion = false;

        try
        {
            removedCompletion = finishedToday.Remove(name);
            if (!removedCompletion)
            {
                return;
            }

            // Ask vanilla to pick the next valid Angler quest, but temporarily use
            // single-player net mode so AnglerQuestSwap does not broadcast it to
            // every connected player.
            _netMode.SetValue(null, 0);
            _anglerQuestSwap.Invoke(null, null);
            _netMode.SetValue(null, oldNetMode);

            // NetMessage.SendData serializes Main.anglerQuest and the per-name
            // completion state. At this moment the new quest is selected and this
            // player's completion marker is absent, so the unmodified client sees
            // a fresh, incomplete quest. Only the completing player receives it.
            var parameters = _sendData.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = _anglerQuestMessageId;
            arguments[1] = whoAmI;
            arguments[2] = -1;
            arguments[3] = _fromLiteral.Invoke(null, new object[] { name });

            for (var index = 4; index < parameters.Length; index++)
            {
                arguments[index] = parameters[index].ParameterType == typeof(float) ? (object)0f : 0;
            }

            _sendData.Invoke(null, arguments);
        }
        catch (Exception ex)
        {
            // Fall back toward vanilla state if our private reroll fails. Do not let
            // a mod-side compatibility problem kill the server's packet loop.
            if (removedCompletion && !finishedToday.Contains(name))
            {
                finishedToday.Add(name);
            }

            Console.Error.WriteLine("[Infinite Angler] Quest reroll failed: " + Unwrap(ex));
        }
        finally
        {
            _netMode.SetValue(null, oldNetMode);
            _anglerQuest.SetValue(null, oldQuest);
            _anglerQuestFinished.SetValue(null, oldQuestFinished);
        }
    }

    private static bool IsCompletionPacket(object messageBuffer, int start)
    {
        var buffer = (byte[])_readBuffer.GetValue(messageBuffer);
        return buffer != null &&
               start >= 0 &&
               start < buffer.Length &&
               buffer[start] == (byte)_anglerQuestFinishedMessageId;
    }

    private static bool TryGetPlayerName(object messageBuffer, out string name)
    {
        name = null;

        var whoAmI = (int)_whoAmI.GetValue(messageBuffer);
        var players = (Array)_players.GetValue(null);
        if (players == null || whoAmI < 0 || whoAmI >= players.Length)
        {
            return false;
        }

        var player = players.GetValue(whoAmI);
        if (player == null)
        {
            return false;
        }

        var nameField = AccessTools.Field(player.GetType(), "name");
        if (nameField == null || nameField.FieldType != typeof(string))
        {
            return false;
        }

        name = nameField.GetValue(player) as string;
        return !string.IsNullOrEmpty(name);
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
        var field = type.GetField(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(type.FullName, name);

        var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return Convert.ToInt32(value);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException invocation && invocation.InnerException != null)
        {
            exception = invocation.InnerException;
        }

        return exception;
    }
}
#else
// Infinite Angler is intentionally server-authoritative. When gloader starts the
// visible Terraria client this source compiles to a no-op. Host & Play automatically
// launches a second gloader instance for TerrariaServer.exe, where the server half
// above is compiled and applied. Joining clients remain completely vanilla.
public static class Mod
{
    public static void Load()
    {
    }
}
#endif
