namespace Terraria
{
    public class Main
    {
        public static int netMode;
        public static int maxTilesX;
        public static int maxTilesY;
        public static Player[] player;
        public static Tile[,] tile;
    }

    public class Player
    {
        public bool active;
        public string name;
        public int selectedItem;
        public Item[] inventory;
        public Vector2 position;
    }

    public class Item
    {
        public int type;
    }

    public class Tile
    {
        public byte liquid;

        public int liquidType()
        {
            return 0;
        }
    }

    public struct Vector2
    {
        public float X;
        public float Y;
    }
}
