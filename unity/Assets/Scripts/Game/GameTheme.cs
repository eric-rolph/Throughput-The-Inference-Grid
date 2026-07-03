using UnityEngine;
using Throughput.Sim;

namespace Throughput.Game
{
    public static class GameTheme
    {
        public static readonly Color Background = Hex("0D0F15");
        public static readonly Color FloorTile = Hex("13161F");
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.045f);
        public static readonly Color TextDim = Hex("6C7A92");
        public static readonly Color TextBright = Hex("D7E3F4");
        public static readonly Color Danger = Hex("FF5C5C");
        public static readonly Color Ok = Hex("58E1C1");
        public static readonly Color Warn = Hex("F2B84B");
        public static readonly Color PanelBg = new Color(0.035f, 0.045f, 0.075f, 0.94f);
        public static readonly Color ChipBg = new Color(0.10f, 0.14f, 0.22f, 1f);

        public static readonly Color JobCyan = Hex("58C7E1");
        public static readonly Color JobPurple = Hex("C46CFF");

        public static readonly Color RackCpu = Hex("2E4A66");
        public static readonly Color RackGpu = Hex("4A2E66");
        public static readonly Color PduBody = Hex("6B5A1E");
        public static readonly Color PduRing = new Color(0.95f, 0.8f, 0.3f, 0.9f);
        public static readonly Color CracBody = Hex("1E5A6B");
        public static readonly Color CracRing = new Color(0.35f, 0.75f, 0.95f, 0.9f);
        public static readonly Color UplinkBody = Hex("1E6B54");
        public static readonly Color FeedBody = Hex("55606F");
        public static readonly Color CableColor = new Color(0.28f, 0.32f, 0.42f, 0.55f);

        public static Color BuildingColor(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.CpuRack: return RackCpu;
                case BuildingKind.GpuRack: return RackGpu;
                case BuildingKind.Pdu: return PduBody;
                case BuildingKind.Crac: return CracBody;
                case BuildingKind.Uplink: return UplinkBody;
                default: return FeedBody;
            }
        }

        /// Floor heat tint: transparent at ambient → orange → red.
        public static Color32 HeatColor(float temp)
        {
            float t = Mathf.InverseLerp(Balance.AmbientTemp, Balance.CriticalTemp, temp);
            if (t <= 0.02f) return new Color32(30, 60, 120, 20);
            Color c = t < 0.5f
                ? Color.Lerp(new Color(0.1f, 0.3f, 0.7f), new Color(0.95f, 0.55f, 0.1f), t * 2f)
                : Color.Lerp(new Color(0.95f, 0.55f, 0.1f), new Color(1f, 0.15f, 0.1f), (t - 0.5f) * 2f);
            c.a = 0.12f + 0.5f * t;
            return c;
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out Color c);
            return c;
        }
    }
}
