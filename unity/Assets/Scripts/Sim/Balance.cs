namespace Throughput.Sim
{
    public enum BuildingKind { GridFeed = 0, Uplink = 1, Pdu = 2, CpuRack = 3, GpuRack = 4, Crac = 5 }

    public struct BuildingSpec
    {
        public string Name;
        public float Cost;
        public float DrawKw;        // power consumed while on
        public float HeatKw;        // heat stamp amplitude source
        public float CoolKw;        // cooling stamp amplitude source (CRAC)
        public float ComputePf;     // capacity in PF (racks)
        public float BandwidthGbps; // uplink share (racks)
        public float RevenuePerSec; // $/s when fully fed
        public float Radius;        // service radius (PDU power / CRAC cooling)
        public float PduCapKw;      // PDU only
        public bool IsRack;
        public bool ServesPurple;   // GPU only
        public float BootSeconds;
    }

    /// Every gameplay constant, per docs/DESIGN2.md §6. One file, tune here only.
    public static class Balance
    {
        public const int GridW = 24;
        public const int GridH = 16;
        public const int TickRate = 20;
        public const float TickDt = 1f / TickRate;

        public const float StartingCash = 10000f;
        public const float FeedCapKw = 500f;
        public const float UplinkGbps = 10f;

        // Heat model
        public const float AmbientTemp = 24f;
        public const float DegPerHeatKw = 1.0f;
        public const float DegPerCoolKw = 0.6f;
        public const float HeatRadius = 3f;
        public const float WarmTemp = 45f;   // -25% throttle
        public const float HotTemp = 60f;    // -50% throttle
        public const float CriticalTemp = 75f; // shutdown
        public const float WarmThrottle = 0.75f;
        public const float HotThrottle = 0.5f;
        public const float RunawayMult = 1.2f;
        public const float HeatRestartCost = 400f;
        public const int HeatRebuildTicks = 10; // 2 Hz

        // Power
        public const float AmberAt = 0.9f;
        public const float TripSeconds = 3f;
        public const float DarkSeconds = 8f;
        public const float RebootStagger = 0.5f;

        // Clock & price ($ per kW-second)
        public const float DaySeconds = 180f;
        public const float StartHour = 6f;
        public const float PriceMid = 0.05f, PriceAmp = 0.03f;      // day 2+
        public const float Day1Mid = 0.03f, Day1Amp = 0.01f;        // flattened day 1

        // Demand
        public const float DemandBasePf = 4f;
        public const float DemandPerDayPf = 2f;
        public const float PurpleShareBase = 0.15f;
        public const float PurpleSharePerDay = 0.10f;
        public const float PurpleShareCap = 0.70f;

        // Progression
        public const float GpuEarnedGate = 1200f;
        public const float LifetimeGate = 15000f;

        // Commerce
        public const float SellFullRefundSeconds = 5f;
        public const float SellRefundFraction = 0.5f;
        public const float UndoWindowSeconds = 10f;
        public const float UplinkUpgradeCost = 3000f;
        public const float UplinkUpgradeGbps = 10f;
        public const float SubstationCost = 12000f;
        public const float SubstationLeadSeconds = 90f;
        public const float SubstationKw = 500f;

        public static readonly BuildingSpec[] Specs =
        {
            new BuildingSpec { Name = "Grid Feed", Cost = 0, DrawKw = 0, BootSeconds = 0 },
            new BuildingSpec { Name = "Fiber Uplink", Cost = 0, DrawKw = 5, BootSeconds = 0 },
            new BuildingSpec { Name = "PDU", Cost = 2000, DrawKw = 5, HeatKw = 2, Radius = 3f,
                               PduCapKw = 100f, BootSeconds = 1f },
            new BuildingSpec { Name = "CPU Rack", Cost = 800, DrawKw = 10, HeatKw = 10,
                               ComputePf = 1f, BandwidthGbps = 1f, RevenuePerSec = 2.0f,
                               IsRack = true, BootSeconds = 3f },
            new BuildingSpec { Name = "GPU Rack", Cost = 5000, DrawKw = 40, HeatKw = 40,
                               ComputePf = 5f, BandwidthGbps = 4f, RevenuePerSec = 6.0f,
                               IsRack = true, ServesPurple = true, BootSeconds = 3f },
            new BuildingSpec { Name = "CRAC", Cost = 1500, DrawKw = 25, CoolKw = 100f,
                               Radius = 3f, BootSeconds = 2f },
        };

        public static BuildingSpec Spec(BuildingKind k) => Specs[(int)k];

        /// $/kW-second at a given game-day + hour-of-day.
        public static float Price(int day, float hour)
        {
            float mid = day <= 1 ? Day1Mid : PriceMid;
            float amp = day <= 1 ? Day1Amp : PriceAmp;
            // trough 06:00, peak 18:00
            return mid - amp * UnityEngine.Mathf.Cos((hour - StartHour) / 24f * 2f * UnityEngine.Mathf.PI);
        }

        public static float DemandTotalPf(float elapsedDays) =>
            DemandBasePf + DemandPerDayPf * elapsedDays;

        public static float PurpleShare(int day) =>
            UnityEngine.Mathf.Min(PurpleShareCap, PurpleShareBase + PurpleSharePerDay * (day - 1));
    }
}
