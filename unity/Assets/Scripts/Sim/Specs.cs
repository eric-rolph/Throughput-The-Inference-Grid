namespace Throughput.Sim
{
    public enum PacketClass { Trivial = 0, Standard = 1, Complex = 2 }

    public enum NodeKind
    {
        Ingress = 0,
        ModelS = 1,
        ModelM = 2,
        ModelL = 3,
        LoadBalancer = 4,
        ClassSwitch = 5,
    }

    public struct ClassSpec
    {
        public string Name;
        public float BasePay;
        public float FullPayLatency;   // A: seconds — full pay at or under
        public float HalfPayLatency;   // B: seconds — 50% pay
        public bool MeasureAtServeStart; // Trivial = TTFT
    }

    public struct NodeSpec
    {
        public string Name;
        public int Size;            // tiles per side (1 or 2)
        public float Cost;
        public float UpkeepPerMin;
        public int Slots;           // 0 = logic node (no service)
        public int QueueDepth;
        public float ColdStartSeconds;
        public int MaxOutLinks;
        // Service seconds per class; negative = cannot serve.
        public float ServiceTrivial;
        public float ServiceStandard;
        public float ServiceComplex;

        public bool IsServer => Slots > 0;

        public float ServiceSeconds(PacketClass c)
        {
            switch (c)
            {
                case PacketClass.Trivial: return ServiceTrivial;
                case PacketClass.Standard: return ServiceStandard;
                default: return ServiceComplex;
            }
        }

        public bool CanServe(PacketClass c) => IsServer && ServiceSeconds(c) > 0f;
    }

    /// All gameplay constants (from docs/DESIGN.md §1.5, §1.6).
    public static class Tuning
    {
        public const int TickRate = 20;
        public const float TickDt = 1f / TickRate;

        public const float LinkSpeedCopper = 12f;     // tiles/s
        public const float LinkSpacing = 0.4f;        // tiles between packets
        public const float LinkCostPerTile = 2f;

        public const float BatchOccupancyPenalty = 0.15f;  // ×(1 + p·(occ−1))
        public const float ClogHeatRampSeconds = 1.0f;

        public const int IngressBacklogRingThreshold = 6;
        public const float RingFillSeconds = 40f;
        public const float RingDrainSeconds = 20f;
        public const int BreachesToFail = 3;

        public const float RefundFraction = 0.5f;

        public static readonly ClassSpec[] Classes =
        {
            new ClassSpec { Name = "Trivial",  BasePay = 1.00f, FullPayLatency = 1.1f, HalfPayLatency = 2.5f, MeasureAtServeStart = true },
            new ClassSpec { Name = "Standard", BasePay = 3.00f, FullPayLatency = 2.6f, HalfPayLatency = 5.0f, MeasureAtServeStart = false },
            new ClassSpec { Name = "Complex",  BasePay = 8.00f, FullPayLatency = 4.0f, HalfPayLatency = 9.0f, MeasureAtServeStart = false },
        };

        public static readonly NodeSpec[] Nodes =
        {
            new NodeSpec { Name = "Ingress", Size = 1, Cost = 0, UpkeepPerMin = 0, Slots = 0, QueueDepth = 0,
                           ColdStartSeconds = 0, MaxOutLinks = 1,
                           ServiceTrivial = -1, ServiceStandard = -1, ServiceComplex = -1 },
            new NodeSpec { Name = "Model S", Size = 2, Cost = 200, UpkeepPerMin = 3, Slots = 1, QueueDepth = 2,
                           ColdStartSeconds = 5, MaxOutLinks = 0,
                           ServiceTrivial = 1.0f, ServiceStandard = -1, ServiceComplex = -1 },
            new NodeSpec { Name = "Model M", Size = 2, Cost = 600, UpkeepPerMin = 8, Slots = 2, QueueDepth = 2,
                           ColdStartSeconds = 20, MaxOutLinks = 0,
                           ServiceTrivial = 0.6f, ServiceStandard = 1.5f, ServiceComplex = -1 },
            new NodeSpec { Name = "Model L", Size = 2, Cost = 2000, UpkeepPerMin = 25, Slots = 4, QueueDepth = 2,
                           ColdStartSeconds = 60, MaxOutLinks = 0,
                           ServiceTrivial = 0.5f, ServiceStandard = 1.0f, ServiceComplex = 2.5f },
            new NodeSpec { Name = "Load Balancer", Size = 1, Cost = 150, UpkeepPerMin = 2, Slots = 0, QueueDepth = 6,
                           ColdStartSeconds = 1, MaxOutLinks = 4,
                           ServiceTrivial = -1, ServiceStandard = -1, ServiceComplex = -1 },
            new NodeSpec { Name = "Class Switch", Size = 1, Cost = 100, UpkeepPerMin = 1, Slots = 0, QueueDepth = 6,
                           ColdStartSeconds = 1, MaxOutLinks = 4,
                           ServiceTrivial = -1, ServiceStandard = -1, ServiceComplex = -1 },
        };

        public static NodeSpec Spec(NodeKind k) => Nodes[(int)k];

        /// Payout multiplier by latency (piecewise: 1 → 0.5 → 0.125 floor).
        public static float TimeFactor(PacketClass c, float latencySeconds)
        {
            ClassSpec s = Classes[(int)c];
            float a = s.FullPayLatency, b = s.HalfPayLatency;
            if (latencySeconds <= a) return 1f;
            if (latencySeconds <= b) return 1f - 0.5f * (latencySeconds - a) / (b - a);
            if (latencySeconds <= 2f * b) return 0.5f - 0.375f * (latencySeconds - b) / b;
            return 0.125f;
        }
    }
}
