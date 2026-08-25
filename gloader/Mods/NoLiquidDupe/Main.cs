#if GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

public static class Mod
{
    public static void Load()
    {
        NoLiquidDupeRuntime.Initialize();
        Console.WriteLine("[No Liquid Dupe] Server-side regular-bucket liquid conservation enabled.");
    }
}

[HarmonyPatch]
internal static class NoLiquidDupeNetLiquidPatch
{
    private static MethodBase TargetMethod()
    {
        return NoLiquidDupeRuntime.DeserializeMethod;
    }

    [HarmonyPrefix]
    private static void Prefix(int __1, ref NoLiquidDupeRuntime.Snapshot __state)
    {
        __state = NoLiquidDupeRuntime.Capture(__1);
    }

    [HarmonyPostfix]
    private static void Postfix(int __1, NoLiquidDupeRuntime.Snapshot __state)
    {
        NoLiquidDupeRuntime.Reconcile(__1, __state);
    }
}

internal static class NoLiquidDupeRuntime
{
    // Vanilla Empty Bucket collection is allowed to become a full bucket after
    // collecting only 100/255 of a tile. A genuinely full scoop removes 255.
    private const int VanillaFillThreshold = 100;
    private const int FullBucketVolume = 255;

    // Bucket reach is much smaller than this. The margin deliberately makes the
    // check insensitive to modest reach changes without snapshotting the world.
    private const int SnapshotRadiusTiles = 24;
    private const int KnownLiquidTypeCount = 4; // water, lava, honey, shimmer

    private static readonly Dictionary<string, int[]> _debtByPlayer =
        new Dictionary<string, int[]>(StringComparer.Ordinal);

    private static readonly HashSet<int> _standardBucketItemIds = new HashSet<int>();

    private static MethodInfo _sendWater;
    private static int _emptyBucketItemId;
    private static int _waterBucketItemId;
    private static int _lavaBucketItemId;
    private static int _honeyBucketItemId;

    public static MethodBase DeserializeMethod { get; private set; }

    internal sealed class Snapshot
    {
        public string PlayerKey;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public byte[] Amounts;
        public sbyte[] Types;
        public int[] Totals;
    }

    public static void Initialize()
    {
        var gameAssembly = typeof(Main).Assembly;

        var liquidModuleType = gameAssembly.GetType(
            "Terraria.GameContent.NetModules.NetLiquidModule",
            throwOnError: true);

        DeserializeMethod = liquidModuleType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
            {
                if (method.Name != "Deserialize" || method.ReturnType != typeof(bool))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(BinaryReader) &&
                       parameters[1].ParameterType == typeof(int);
            })
            ?? throw new MissingMethodException(
                liquidModuleType.FullName,
                "Deserialize(BinaryReader, int)");

        var itemIdType = gameAssembly.GetType("Terraria.ID.ItemID", throwOnError: true);
        _emptyBucketItemId = ReadConstantInt(itemIdType, "EmptyBucket");
        _waterBucketItemId = ReadConstantInt(itemIdType, "WaterBucket");
        _lavaBucketItemId = ReadConstantInt(itemIdType, "LavaBucket");
        _honeyBucketItemId = ReadConstantInt(itemIdType, "HoneyBucket");

        _standardBucketItemIds.Clear();
        _standardBucketItemIds.Add(_emptyBucketItemId);
        _standardBucketItemIds.Add(_waterBucketItemId);
        _standardBucketItemIds.Add(_lavaBucketItemId);
        _standardBucketItemIds.Add(_honeyBucketItemId);

        var netMessageType = gameAssembly.GetType("Terraria.NetMessage", throwOnError: true);
        var sendWaterCandidates = netMessageType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method =>
            {
                if (!string.Equals(method.Name, "sendWater", StringComparison.OrdinalIgnoreCase) ||
                    method.ReturnType != typeof(void))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(int) &&
                       parameters[1].ParameterType == typeof(int);
            })
            .ToArray();

        if (sendWaterCandidates.Length != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one Terraria.NetMessage.sendWater(int, int), found " +
                sendWaterCandidates.Length + ".");
        }

        _sendWater = sendWaterCandidates[0];
    }

    public static Snapshot Capture(int userId)
    {
        if (Main.netMode != 2 ||
            Main.player == null ||
            userId < 0 ||
            userId >= Main.player.Length)
        {
            return null;
        }

        var player = Main.player[userId];
        if (player == null || !player.active || player.inventory == null)
        {
            return null;
        }

        var selected = player.selectedItem;
        if (selected < 0 || selected >= player.inventory.Length)
        {
            return null;
        }

        var heldItem = player.inventory[selected];
        if (heldItem == null || !_standardBucketItemIds.Contains(heldItem.type))
        {
            return null;
        }

        if (Main.tile == null || Main.maxTilesX <= 0 || Main.maxTilesY <= 0)
        {
            return null;
        }

        var playerTileX = (int)(player.position.X / 16f);
        var playerTileY = (int)(player.position.Y / 16f);
        var x0 = Math.Max(0, playerTileX - SnapshotRadiusTiles);
        var y0 = Math.Max(0, playerTileY - SnapshotRadiusTiles);
        var x1 = Math.Min(Main.maxTilesX - 1, playerTileX + SnapshotRadiusTiles);
        var y1 = Math.Min(Main.maxTilesY - 1, playerTileY + SnapshotRadiusTiles);

        if (x1 < x0 || y1 < y0)
        {
            return null;
        }

        var width = x1 - x0 + 1;
        var height = y1 - y0 + 1;
        var cellCount = checked(width * height);
        var snapshot = new Snapshot
        {
            PlayerKey = string.IsNullOrEmpty(player.name) ? "slot:" + userId : player.name,
            X = x0,
            Y = y0,
            Width = width,
            Height = height,
            Amounts = new byte[cellCount],
            Types = new sbyte[cellCount],
            Totals = new int[KnownLiquidTypeCount]
        };

        for (var localY = 0; localY < height; localY++)
        {
            for (var localX = 0; localX < width; localX++)
            {
                var index = localY * width + localX;
                var tile = Main.tile[x0 + localX, y0 + localY];
                ReadTile(tile, out var amount, out var liquidType);
                snapshot.Amounts[index] = amount;
                snapshot.Types[index] = (sbyte)liquidType;

                if (amount > 0 && liquidType >= 0 && liquidType < KnownLiquidTypeCount)
                {
                    snapshot.Totals[liquidType] += amount;
                }
            }
        }

        return snapshot;
    }

    public static void Reconcile(int userId, Snapshot before)
    {
        if (before == null || Main.netMode != 2)
        {
            return;
        }

        // Do not let a stale slot snapshot become attached to a different player.
        if (Main.player == null || userId < 0 || userId >= Main.player.Length)
        {
            return;
        }

        var player = Main.player[userId];
        if (player == null || !player.active)
        {
            return;
        }

        var playerKey = string.IsNullOrEmpty(player.name) ? "slot:" + userId : player.name;
        if (!string.Equals(playerKey, before.PlayerKey, StringComparison.Ordinal))
        {
            return;
        }

        var afterTotals = new int[KnownLiquidTypeCount];
        var sawUnknownLiquidType = false;

        for (var localY = 0; localY < before.Height; localY++)
        {
            for (var localX = 0; localX < before.Width; localX++)
            {
                var tile = Main.tile[before.X + localX, before.Y + localY];
                ReadTile(tile, out var amount, out var liquidType);

                if (amount == 0)
                {
                    continue;
                }

                if (liquidType < 0 || liquidType >= KnownLiquidTypeCount)
                {
                    sawUnknownLiquidType = true;
                    continue;
                }

                afterTotals[liquidType] += amount;
            }
        }

        if (sawUnknownLiquidType)
        {
            return;
        }

        var changedType = -1;
        var delta = 0;
        for (var liquidType = 0; liquidType < KnownLiquidTypeCount; liquidType++)
        {
            var candidateDelta = afterTotals[liquidType] - before.Totals[liquidType];
            if (candidateDelta == 0)
            {
                continue;
            }

            // Mixing two liquid types can legitimately consume liquid and create
            // blocks. It is not the bucket duplication case, so leave it alone.
            if (changedType != -1)
            {
                return;
            }

            changedType = liquidType;
            delta = candidateDelta;
        }

        // A normal bucket cannot collect shimmer and there is no regular Shimmer
        // Bucket. If shimmer moved while a bucket happened to be selected, ignore it.
        if (changedType < 0 || changedType == 3)
        {
            return;
        }

        if (delta < 0)
        {
            var removed = -delta;

            // This is the vanilla cheese: 100..254 units disappear but the client
            // receives a completely full regular bucket. Remember only the amount
            // vanilla created out of thin air. A proper 255-unit scoop owes nothing.
            if (removed >= VanillaFillThreshold && removed < FullBucketVolume)
            {
                AddDebt(before.PlayerKey, changedType, FullBucketVolume - removed);
            }

            return;
        }

        if (delta <= 0)
        {
            return;
        }

        var debt = GetDebt(before.PlayerKey, changedType);
        if (debt <= 0)
        {
            return;
        }

        var correction = Math.Min(delta, debt);
        var corrected = RemoveOnlyNewlyAddedLiquid(before, changedType, correction);
        if (corrected <= 0)
        {
            return;
        }

        SetDebt(before.PlayerKey, changedType, debt - corrected);
    }

    private static int RemoveOnlyNewlyAddedLiquid(
        Snapshot before,
        int liquidType,
        int amountToRemove)
    {
        var remaining = amountToRemove;

        for (var localY = 0; localY < before.Height && remaining > 0; localY++)
        {
            for (var localX = 0; localX < before.Width && remaining > 0; localX++)
            {
                var index = localY * before.Width + localX;
                var x = before.X + localX;
                var y = before.Y + localY;
                var tile = Main.tile[x, y];
                ReadTile(tile, out var afterAmount, out var afterType);

                if (afterAmount == 0 || afterType != liquidType)
                {
                    continue;
                }

                var beforeAmount = before.Amounts[index];
                var beforeType = before.Types[index];
                var baseline = beforeAmount > 0 && beforeType == liquidType
                    ? beforeAmount
                    : 0;

                if (afterAmount <= baseline)
                {
                    continue;
                }

                var newlyAdded = afterAmount - baseline;
                var take = Math.Min(newlyAdded, remaining);
                tile.liquid = (byte)(afterAmount - take);
                remaining -= take;

                try
                {
                    // Ask vanilla to serialize the corrected tile using whatever
                    // liquid network representation this Terraria build expects.
                    _sendWater.Invoke(null, new object[] { x, y });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "[No Liquid Dupe] Failed to synchronize corrected liquid tile: " +
                        Unwrap(ex));
                }
            }
        }

        return amountToRemove - remaining;
    }

    private static void ReadTile(Tile tile, out byte amount, out int liquidType)
    {
        if (tile == null)
        {
            amount = 0;
            liquidType = -1;
            return;
        }

        amount = tile.liquid;
        liquidType = amount == 0 ? -1 : tile.liquidType();
    }

    private static void AddDebt(string playerKey, int liquidType, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var debts = GetOrCreateDebts(playerKey);
        var newValue = (long)debts[liquidType] + amount;
        debts[liquidType] = newValue > int.MaxValue ? int.MaxValue : (int)newValue;
    }

    private static int GetDebt(string playerKey, int liquidType)
    {
        return _debtByPlayer.TryGetValue(playerKey, out var debts)
            ? debts[liquidType]
            : 0;
    }

    private static void SetDebt(string playerKey, int liquidType, int value)
    {
        var debts = GetOrCreateDebts(playerKey);
        debts[liquidType] = Math.Max(0, value);
    }

    private static int[] GetOrCreateDebts(string playerKey)
    {
        if (!_debtByPlayer.TryGetValue(playerKey, out var debts))
        {
            debts = new int[KnownLiquidTypeCount];
            _debtByPlayer[playerKey] = debts;
        }

        return debts;
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
// No Liquid Dupe is deliberately server-authoritative. The visible Host & Play
// client compiles this file to a no-op; gloader's redirected TerrariaServer.exe
// compiles and applies the patch. Joining players do not install anything.
public static class Mod
{
    public static void Load()
    {
    }
}
#endif
