using Terraria.Localization;

namespace Terraria.ID
{
    public static class MessageID
    {
        // Deliberately NOT vanilla's current 74/75/76 values.
        // The patcher must discover these constants instead of hardcoding them.
        public const byte AnglerQuest = 174;
        public const byte AnglerQuestFinished = 175;
        public const byte QuestsCountSync = 176;
    }
}

namespace Terraria
{
    public static class Main
    {
        public static int netMode = 2;
        public static int anglerQuest = 7;
        public static bool anglerQuestFinished = true;
        public static List<string> anglerWhoFinishedToday = new();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();

        public static void AnglerQuestSwap()
        {
            // Vanilla-shaped state mutation: a quest swap resets the global finished flag
            // and selects a new valid quest. It must NOT touch the per-name completion list.
            anglerQuestFinished = false;
            anglerQuest = (anglerQuest + 1) % 40;
        }
    }

    public sealed class Player
    {
        public string name = string.Empty;
        public int anglerQuestsFinished;
    }

    public sealed class MessageBuffer
    {
        public int whoAmI;

        public void GetData(int start, int length, out int messageType)
        {
            messageType = ID.MessageID.AnglerQuestFinished;
            if (Main.netMode != 2)
                return;

            string name = Main.player[whoAmI].name;
            if (Main.anglerWhoFinishedToday.Contains(name))
                return;

            Main.anglerWhoFinishedToday.Add(name);
        }
    }

    public static class NetMessage
    {
        public static readonly List<(int MessageType, int RemoteClient, int Quest, bool Completed)> Sent = new();

        public static void SendData(
            int msgType,
            int remoteClient = -1,
            int ignoreClient = -1,
            NetworkText? text = null,
            int number = 0,
            float number2 = 0f,
            float number3 = 0f,
            float number4 = 0f,
            int number5 = 0,
            int number6 = 0,
            int number7 = 0)
        {
            if (msgType == ID.MessageID.AnglerQuest)
            {
                Sent.Add((
                    msgType,
                    remoteClient,
                    Main.anglerQuest,
                    Main.anglerWhoFinishedToday.Contains(text!.ToString())));
            }
        }
    }
}

namespace Terraria.Localization
{
    public sealed class NetworkText
    {
        private readonly string _text;
        private NetworkText(string text) => _text = text;
        public static NetworkText FromLiteral(string text) => new(text);
        public override string ToString() => _text;
    }
}

namespace Fixture
{
    internal sealed class VanillaGuest
    {
        public int Quest { get; private set; } = -1;
        public bool Completed { get; private set; } = true;
        public bool CanQuest => !Completed;

        // Never patched: this is an ordinary Terraria client consuming the vanilla packet state.
        public void ReceiveQuestPacket(int quest, bool completed)
        {
            Quest = quest;
            Completed = completed;
        }
    }

    internal static class Program
    {
        public static int Main()
        {
            Terraria.Main.netMode = 2;
            Terraria.Main.anglerQuest = 7;
            Terraria.Main.anglerQuestFinished = true;
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.anglerWhoFinishedToday.Add("AlreadyDone");
            Terraria.Main.player[1].name = "VanillaGuest";
            Terraria.Main.player[2].name = "SecondGuest";
            Terraria.NetMessage.Sent.Clear();

            Complete(1);
            Require(Terraria.Main.netMode == 2, "host netMode was not restored after guest 1");
            Require(Terraria.Main.anglerQuest == 7, "host global quest was not restored after guest 1");
            Require(Terraria.Main.anglerQuestFinished, "host global anglerQuestFinished was not restored after guest 1");
            Require(Terraria.Main.anglerWhoFinishedToday.Contains("AlreadyDone"), "another player's completion state was lost");
            Require(!Terraria.Main.anglerWhoFinishedToday.Contains("VanillaGuest"), "guest 1 cooldown name remained recorded");

            Complete(2);
            Require(Terraria.Main.netMode == 2, "host netMode was not restored after guest 2");
            Require(Terraria.Main.anglerQuest == 7, "host global quest was not restored after guest 2");
            Require(Terraria.Main.anglerQuestFinished, "host global anglerQuestFinished was not restored after guest 2");
            Require(Terraria.Main.anglerWhoFinishedToday.Contains("AlreadyDone"), "existing completion state was altered by guest 2");
            Require(!Terraria.Main.anglerWhoFinishedToday.Contains("SecondGuest"), "guest 2 cooldown name remained recorded");

            // Repeat a completion for guest 1 to prove the server-side cooldown really stays removed.
            Complete(1);

            Require(Terraria.NetMessage.Sent.Count == 3, $"expected three private quest packets, got {Terraria.NetMessage.Sent.Count}");
            AssertPacket(0, 1);
            AssertPacket(1, 2);
            AssertPacket(2, 1);

            var vanillaGuest = new VanillaGuest();
            var first = Terraria.NetMessage.Sent[0];
            vanillaGuest.ReceiveQuestPacket(first.Quest, first.Completed);
            Require(vanillaGuest.Quest == 8, "unmodified guest did not receive the rerolled quest");
            Require(vanillaGuest.CanQuest, "unmodified guest remained cooldown-locked");

            Console.WriteLine("PASS: patched server handled two vanilla guests, repeated quests, dynamic MessageID, and preserved shared host state.");
            return 0;
        }

        private static void Complete(int whoAmI)
        {
            var buffer = new Terraria.MessageBuffer { whoAmI = whoAmI };
            buffer.GetData(0, 0, out int messageType);
            Require(messageType == Terraria.ID.MessageID.AnglerQuestFinished, "fixture completion message ID changed unexpectedly");
        }

        private static void AssertPacket(int index, int expectedClient)
        {
            var packet = Terraria.NetMessage.Sent[index];
            Require(packet.MessageType == Terraria.ID.MessageID.AnglerQuest, $"packet {index} used a hardcoded/wrong message ID");
            Require(packet.RemoteClient == expectedClient, $"packet {index} targeted client {packet.RemoteClient}, expected {expectedClient}");
            Require(packet.Quest == 8, $"packet {index} carried quest {packet.Quest}, expected rerolled quest 8");
            Require(!packet.Completed, $"packet {index} still marked the completing guest finished");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
