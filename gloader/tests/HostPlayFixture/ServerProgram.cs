using System;
using System.Collections.Generic;
using System.Linq;

namespace Terraria.ID
{
    public static class MessageID
    {
        public const byte AnglerQuestFinished = 175;
    }
}

namespace Terraria
{
    public static class Main
    {
        public static int netMode = 2;
        public static int anglerQuest = 7;
        public static bool anglerQuestFinished = true;
        public static List<string> anglerWhoFinishedToday = new List<string>();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();

        public static bool triggerDawnReset;
        public static int questSwapCount;

        // Synthetic vanilla dawn behavior. Infinite Angler should suppress only the
        // Clear + AnglerQuestSwap pair while leaving this method callable normally.
        public static void UpdateTime()
        {
            if (triggerDawnReset)
            {
                triggerDawnReset = false;
                anglerWhoFinishedToday.Clear();
                AnglerQuestSwap();
            }
        }

        public static void AnglerQuestSwap()
        {
            anglerQuestFinished = false;
            anglerQuest = (anglerQuest + 1) % 40;
            questSwapCount++;
        }
    }

    public sealed class Player
    {
        public bool active;
        public string name = string.Empty;
    }

    public sealed class MessageBuffer
    {
        public byte[] readBuffer = new byte[256];
        public int whoAmI;

        public void GetData(int start, int length, out int messageType)
        {
            messageType = readBuffer[start];
            if (messageType != ID.MessageID.AnglerQuestFinished || Main.netMode != 2)
                return;

            var player = Main.player[whoAmI];
            if (!player.active || string.IsNullOrEmpty(player.name))
                return;

            if (!Main.anglerWhoFinishedToday.Contains(player.name))
                Main.anglerWhoFinishedToday.Add(player.name);
        }
    }
}

namespace FixtureServer
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            Require(
                args.Length == 2 && args[0] == "--fixture-arg" && args[1] == "hello world",
                "Host & Play redirect did not preserve the original server arguments.");

            ResetFixture();

            // One of two connected players finishes. The round must stay put.
            Complete(1);
            Tick();
            Require(Terraria.Main.anglerQuest == 7, "quest advanced before every connected player finished");
            Require(Terraria.Main.questSwapCount == 0, "quest swap ran too early");
            Require(Terraria.Main.anglerWhoFinishedToday.SequenceEqual(new[] { "VanillaGuest" }),
                "first player's vanilla completion marker was not preserved");

            // Dawn must no longer reset either the quest or the completion list.
            Terraria.Main.triggerDawnReset = true;
            Tick();
            Require(Terraria.Main.anglerQuest == 7, "dawn changed the Angler quest");
            Require(Terraria.Main.questSwapCount == 0, "dawn still called AnglerQuestSwap");
            Require(Terraria.Main.anglerWhoFinishedToday.SequenceEqual(new[] { "VanillaGuest" }),
                "dawn cleared the current round's completion state");

            // The second player completes; now the whole shared round advances once.
            Complete(2);
            Tick();
            Require(Terraria.Main.anglerQuest == 8, "all-player completion did not advance the quest");
            Require(Terraria.Main.questSwapCount == 1, "all-player completion did not perform exactly one swap");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "new shared round did not clear the completion list");

            // A player joining during a round counts immediately and blocks advancement
            // until they complete the same shared quest.
            Connect(3, "LateGuest");
            Complete(1);
            Complete(2);
            Tick();
            Require(Terraria.Main.anglerQuest == 8, "late joiner did not count toward the current round");

            Complete(3);
            Tick();
            Require(Terraria.Main.anglerQuest == 9, "round did not advance after late joiner completed");
            Require(Terraria.Main.questSwapCount == 2, "late-join round swapped an unexpected number of times");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "late-join round did not start cleanly");

            // A disconnected player stops counting immediately. If everyone remaining
            // is already done, the next server tick advances the round.
            Complete(1);
            Complete(2);
            Tick();
            Require(Terraria.Main.anglerQuest == 9, "round advanced while connected third player was incomplete");

            Terraria.Main.player[3].active = false;
            Tick();
            Require(Terraria.Main.anglerQuest == 10, "disconnect did not release the blocked round");
            Require(Terraria.Main.questSwapCount == 3, "disconnect-triggered round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "disconnect-triggered round did not start cleanly");

            // With only one connected player, that player's completion is the whole group.
            Terraria.Main.player[2].active = false;
            Complete(1);
            Tick();
            Require(Terraria.Main.anglerQuest == 11, "single connected player did not advance the shared quest");
            Require(Terraria.Main.questSwapCount == 4, "single-player round swapped incorrectly");
            Require(Terraria.Main.anglerWhoFinishedToday.Count == 0,
                "single-player round did not clear completion state");

            Console.WriteLine(
                "PASS: Host & Play child was routed through gloader; Infinite Angler preserved quests across dawn, advanced only after all active players completed, counted joiners, ignored disconnects, and handled a one-player group.");
            return 0;
        }

        private static void ResetFixture()
        {
            Terraria.Main.netMode = 2;
            Terraria.Main.anglerQuest = 7;
            Terraria.Main.anglerQuestFinished = true;
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.questSwapCount = 0;
            Terraria.Main.triggerDawnReset = false;

            foreach (var player in Terraria.Main.player)
            {
                player.active = false;
                player.name = string.Empty;
            }

            Connect(1, "VanillaGuest");
            Connect(2, "SecondGuest");
        }

        private static void Connect(int index, string name)
        {
            Terraria.Main.player[index].active = true;
            Terraria.Main.player[index].name = name;
        }

        private static void Complete(int whoAmI)
        {
            var buffer = new Terraria.MessageBuffer { whoAmI = whoAmI };
            buffer.readBuffer[0] = Terraria.ID.MessageID.AnglerQuestFinished;
            int messageType;
            buffer.GetData(0, 1, out messageType);
            Require(messageType == Terraria.ID.MessageID.AnglerQuestFinished,
                "fixture completion message ID changed unexpectedly");
        }

        private static void Tick()
        {
            Terraria.Main.UpdateTime();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
