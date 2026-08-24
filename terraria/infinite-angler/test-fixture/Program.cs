namespace Terraria;

public sealed class Player
{
    public static int RewardCalls;

    public void GetAnglerReward(object? angler, int questItemType)
    {
        RewardCalls++;
    }
}

public static class Main
{
    public static int netMode;
    public static bool anglerQuestFinished;
    public static readonly List<string> anglerWhoFinishedToday = new();
    public static int anglerQuest;

    public static void AnglerQuestSwap()
    {
        if (netMode == 1)
            return;

        anglerWhoFinishedToday.Clear();
        anglerQuestFinished = false;
        anglerQuest = (anglerQuest + 1) % 10;
    }

    public static void DrawNPCChatButtons()
    {
        if (!anglerQuestFinished && !anglerWhoFinishedToday.Contains("fixture"))
        {
            var player = new Player();
            _ = anglerQuest;
            player.GetAnglerReward(null, 1234);
            anglerQuestFinished = true;
        }
    }
}

namespace Fixture;

internal static class EntryPoint
{
    public static int Main(string[] args)
    {
        Terraria.Main.netMode = 1;
        Terraria.Main.anglerQuestFinished = true;
        Terraria.Main.anglerWhoFinishedToday.Clear();
        Terraria.Main.anglerWhoFinishedToday.Add("fixture");
        Terraria.Main.anglerQuest = 0;
        Terraria.Player.RewardCalls = 0;

        Terraria.Main.DrawNPCChatButtons();

        if (Terraria.Player.RewardCalls != 1)
            throw new InvalidOperationException($"Expected one reward call, got {Terraria.Player.RewardCalls}.");
        if (Terraria.Main.anglerQuest != 1)
            throw new InvalidOperationException($"Expected local reroll to quest 1, got {Terraria.Main.anglerQuest}.");
        if (Terraria.Main.netMode != 1)
            throw new InvalidOperationException($"Expected netMode to be restored to 1, got {Terraria.Main.netMode}.");

        Console.WriteLine("Synthetic Terraria patch exercise passed.");
        return 0;
    }
}
