using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Localization;

namespace Terraria.ID
{
    public static class MessageID
    {
        // Deliberately not the current vanilla values. Infinite Angler must discover
        // these from the assembly instead of hardcoding packet numbers.
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
        public static List<string> anglerWhoFinishedToday = new List<string>();
        public static Player[] player = Enumerable.Range(0, 8).Select(_ => new Player()).ToArray();

        public static void AnglerQuestSwap()
        {
            anglerQuestFinished = false;
            anglerQuest = (anglerQuest + 1) % 40;
        }
    }

    public sealed class Player
    {
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
            {
                return;
            }

            var name = Main.player[whoAmI].name;
            if (Main.anglerWhoFinishedToday.Contains(name))
            {
                return;
            }

            Main.anglerWhoFinishedToday.Add(name);
        }
    }

    public static class NetMessage
    {
        public sealed class SentPacket
        {
            public int MessageType;
            public int RemoteClient;
            public int Quest;
            public bool Completed;
        }

        public static readonly List<SentPacket> Sent = new List<SentPacket>();

        public static void SendData(
            int msgType,
            int remoteClient = -1,
            int ignoreClient = -1,
            NetworkText text = null,
            int number = 0,
            float number2 = 0f,
            float number3 = 0f,
            float number4 = 0f,
            int number5 = 0,
            int number6 = 0,
            int number7 = 0)
        {
            if (msgType != ID.MessageID.AnglerQuest)
            {
                return;
            }

            Sent.Add(new SentPacket
            {
                MessageType = msgType,
                RemoteClient = remoteClient,
                Quest = Main.anglerQuest,
                Completed = Main.anglerWhoFinishedToday.Contains(text == null ? string.Empty : text.ToString())
            });
        }
    }
}

namespace Terraria.Localization
{
    public sealed class NetworkText
    {
        private readonly string _text;

        private NetworkText(string text)
        {
            _text = text;
        }

        public static NetworkText FromLiteral(string text)
        {
            return new NetworkText(text);
        }

        public override string ToString()
        {
            return _text;
        }
    }
}

namespace FixtureServer
{
    internal sealed class VanillaGuest
    {
        public int Quest { get; private set; } = -1;
        public bool Completed { get; private set; } = true;
        public bool CanQuest => !Completed;

        public void ReceiveQuestPacket(int quest, bool completed)
        {
            Quest = quest;
            Completed = completed;
        }
    }

    internal static class Program
    {
        public static int Main(string[] args)
        {
            Require(
                args.Length == 2 && args[0] == "--fixture-arg" && args[1] == "hello world",
                "Host & Play redirect did not preserve the original server arguments.");

            Terraria.Main.netMode = 2;
            Terraria.Main.anglerQuest = 7;
            Terraria.Main.anglerQuestFinished = true;
            Terraria.Main.anglerWhoFinishedToday.Clear();
            Terraria.Main.anglerWhoFinishedToday.Add("AlreadyDone");
            Terraria.Main.player[1].name = "VanillaGuest";
            Terraria.Main.player[2].name = "SecondGuest";
            Terraria.NetMessage.Sent.Clear();

            Complete(1);
            AssertSharedState("guest 1");
            Require(Terraria.Main.anglerWhoFinishedToday.Contains("AlreadyDone"), "another player's completion state was lost");
            Require(!Terraria.Main.anglerWhoFinishedToday.Contains("VanillaGuest"), "guest 1 cooldown name remained recorded");

            Complete(2);
            AssertSharedState("guest 2");
            Require(Terraria.Main.anglerWhoFinishedToday.Contains("AlreadyDone"), "existing completion state was altered by guest 2");
            Require(!Terraria.Main.anglerWhoFinishedToday.Contains("SecondGuest"), "guest 2 cooldown name remained recorded");

            // Repeat guest 1 to prove the daily completion marker really stays removed.
            Complete(1);
            AssertSharedState("guest 1 repeat");

            Require(Terraria.NetMessage.Sent.Count == 3, "expected three private quest packets, got " + Terraria.NetMessage.Sent.Count);
            AssertPacket(0, 1);
            AssertPacket(1, 2);
            AssertPacket(2, 1);

            var vanillaGuest = new VanillaGuest();
            var first = Terraria.NetMessage.Sent[0];
            vanillaGuest.ReceiveQuestPacket(first.Quest, first.Completed);
            Require(vanillaGuest.Quest == 8, "unmodified guest did not receive the rerolled quest");
            Require(vanillaGuest.CanQuest, "unmodified guest remained cooldown-locked");

            Console.WriteLine("PASS: Host & Play child was routed through gloader; Infinite Angler handled two vanilla guests, repeat quests, dynamic packet IDs, and preserved shared host state.");
            return 0;
        }

        private static void Complete(int whoAmI)
        {
            var buffer = new Terraria.MessageBuffer { whoAmI = whoAmI };
            buffer.readBuffer[0] = Terraria.ID.MessageID.AnglerQuestFinished;
            int messageType;
            buffer.GetData(0, 1, out messageType);
            Require(messageType == Terraria.ID.MessageID.AnglerQuestFinished, "fixture completion message ID changed unexpectedly");
        }

        private static void AssertSharedState(string label)
        {
            Require(Terraria.Main.netMode == 2, "host netMode was not restored after " + label);
            Require(Terraria.Main.anglerQuest == 7, "host global quest was not restored after " + label);
            Require(Terraria.Main.anglerQuestFinished, "host global anglerQuestFinished was not restored after " + label);
        }

        private static void AssertPacket(int index, int expectedClient)
        {
            var packet = Terraria.NetMessage.Sent[index];
            Require(packet.MessageType == Terraria.ID.MessageID.AnglerQuest, "packet " + index + " used a hardcoded/wrong message ID");
            Require(packet.RemoteClient == expectedClient, "packet " + index + " targeted client " + packet.RemoteClient + ", expected " + expectedClient);
            Require(packet.Quest == 8, "packet " + index + " carried quest " + packet.Quest + ", expected rerolled quest 8");
            Require(!packet.Completed, "packet " + index + " still marked the completing guest finished");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
