using Terraria.Localization;

namespace Terraria
{
    public static class Main
    {
        public static int netMode = 2;
        public static int anglerQuest = 7;
        public static bool anglerQuestFinished;
        public static List<string> anglerWhoFinishedToday = new();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();

        public static void AnglerQuestSwap()
        {
            // Deterministic stand-in for vanilla's valid quest reroll.
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
            messageType = 75;
            if (Main.netMode != 2)
                return;

            string name = Main.player[whoAmI].name;
            if (Main.anglerWhoFinishedToday.Contains(name))
                return;

            // This exact vanilla-shaped Add is where the host patch injects its completion hook.
            Main.anglerWhoFinishedToday.Add(name);
        }
    }

    public static class NetMessage
    {
        public static int LastMessageType = -1;
        public static int LastRemoteClient = -1;
        public static int LastQuest = -1;
        public static bool LastCompleted = true;

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
            // Preserve the same structural dependencies as vanilla packet 74 serialization.
            if (msgType == 74)
            {
                LastMessageType = msgType;
                LastRemoteClient = remoteClient;
                LastQuest = Main.anglerQuest;
                LastCompleted = Main.anglerWhoFinishedToday.Contains(text!.ToString());
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

        // This class is never patched. It represents an ordinary client consuming packet 74.
        public void ReceiveQuestPacket(byte quest, bool completed)
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
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.player[1].name = "VanillaGuest";

            var buffer = new Terraria.MessageBuffer { whoAmI = 1 };
            buffer.GetData(0, 0, out _);

            Require(Terraria.Main.netMode == 2, "host netMode was not restored");
            Require(Terraria.Main.anglerQuest == 7, "host global quest was not restored");
            Require(!Terraria.Main.anglerWhoFinishedToday.Contains("VanillaGuest"), "guest cooldown name remained recorded");
            Require(Terraria.NetMessage.LastMessageType == 74, "host did not send vanilla Angler packet 74");
            Require(Terraria.NetMessage.LastRemoteClient == 1, "packet 74 was not targeted only to the completing guest");
            Require(Terraria.NetMessage.LastQuest == 8, "next quest was not vanilla-rerolled");
            Require(!Terraria.NetMessage.LastCompleted, "packet 74 still marked the guest completed");

            var guest = new VanillaGuest();
            guest.ReceiveQuestPacket((byte)Terraria.NetMessage.LastQuest, Terraria.NetMessage.LastCompleted);
            Require(guest.Quest == 8, "unmodified guest did not receive the new quest");
            Require(guest.CanQuest, "unmodified guest remained cooldown-locked");

            Console.WriteLine("PASS: host-only patch reset one vanilla guest, sent a private packet 74, and restored shared host state.");
            return 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
