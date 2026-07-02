using UnityEngine;
using Throughput.Sim;

namespace Throughput.Game
{
    public static class GameTheme
    {
        public static readonly Color Background = Hex("101218");
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.035f);
        public static readonly Color LinkCool = Hex("1E5A46");
        public static readonly Color LinkWarm = Hex("B07A1E");
        public static readonly Color LinkHot = Hex("C33131");
        public static readonly Color NodeOutline = Hex("4EA8DE");
        public static readonly Color NodeServer = Hex("58E1C1");
        public static readonly Color NodeLogic = Hex("9D6CFF");
        public static readonly Color NodeIngress = Hex("F2B84B");
        public static readonly Color NodeCold = Hex("3A4356");
        public static readonly Color TextDim = Hex("6C7A92");
        public static readonly Color TextBright = Hex("D7E3F4");
        public static readonly Color Danger = Hex("FF5C5C");
        public static readonly Color Ok = Hex("58E1C1");
        public static readonly Color Warn = Hex("F2B84B");
        public static readonly Color PanelBg = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        public static Color PacketColor(PacketClass c)
        {
            switch (c)
            {
                case PacketClass.Trivial: return Hex("58C7E1");
                case PacketClass.Standard: return Hex("58E17E");
                default: return Hex("E158B9");
            }
        }

        public static Sprite PacketSprite(PacketClass c)
        {
            switch (c)
            {
                case PacketClass.Trivial: return SpriteFactory.Circle;
                case PacketClass.Standard: return SpriteFactory.Triangle;
                default: return SpriteFactory.Square;
            }
        }

        public static Color NodeColor(NodeKind k, bool active)
        {
            if (!active) return NodeCold;
            switch (k)
            {
                case NodeKind.Ingress: return NodeIngress;
                case NodeKind.LoadBalancer:
                case NodeKind.ClassSwitch: return NodeLogic;
                default: return NodeServer;
            }
        }

        public static string NodeGlyph(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Ingress: return ">";
                case NodeKind.ModelS: return "S";
                case NodeKind.ModelM: return "M";
                case NodeKind.ModelL: return "L";
                case NodeKind.LoadBalancer: return "LB";
                default: return "SW";
            }
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out Color c);
            return c;
        }
    }
}
