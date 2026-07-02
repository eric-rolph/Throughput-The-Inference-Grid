using System.Collections.Generic;

namespace Throughput.Sim
{
    public struct RateKey { public float T; public float Rate; }

    public sealed class ClientSpec
    {
        public int IngressX, IngressY;
        public RateKey[] Ramp;                       // scripted offered-rate timeline
        public float[] Mix = { 1f, 0f, 0f };         // Trivial/Standard/Complex shares
        public string Name = "Client";

        public float RateAt(float t)
        {
            if (Ramp == null || Ramp.Length == 0) return 0f;
            if (t <= Ramp[0].T) return Ramp[0].Rate;
            for (int i = 0; i < Ramp.Length - 1; i++)
            {
                if (t < Ramp[i + 1].T)
                {
                    float u = (t - Ramp[i].T) / (Ramp[i + 1].T - Ramp[i].T);
                    return Ramp[i].Rate + u * (Ramp[i + 1].Rate - Ramp[i].Rate);
                }
            }
            return Ramp[Ramp.Length - 1].Rate;
        }
    }

    public sealed class PrePlacedNode
    {
        public NodeKind Kind;
        public int X, Y;
        public int LinkFromClient = -1;   // index into Clients; wires ingress -> this node
    }

    public sealed class SpikeSpec
    {
        public float FirstAt = 180f;      // seconds
        public float Every = 120f;
        public float WarningLead = 20f;
        public float Duration = 15f;
        public float Multiplier = 2f;
    }

    public sealed class ContractSpec
    {
        public int Number;
        public string Title;
        public string Brief;              // intro-card flavor + instructions
        public float StartingCash;
        public NodeKind[] Palette;        // player-placeable nodes
        public ClientSpec[] Clients;
        public PrePlacedNode[] PrePlaced = System.Array.Empty<PrePlacedNode>();
        public SpikeSpec Spikes;          // null = no spikes
        public float SurviveSeconds;      // goal: reach this time...
        public float EarnGoal;            // ...and/or earn this much (0 = ignore)
        public ulong Seed = 0xC0FFEE;

        public static readonly List<ContractSpec> All = new List<ContractSpec>
        {
            new ContractSpec
            {
                Number = 1,
                Title = "Hello, Latency",
                Brief =
                    "Acme Corp streams simple queries (● Trivial) into your ingress.\n\n" +
                    "The previous architect wired it to a distant Model L — watch the packets " +
                    "desaturate as they age and land amber payouts.\n\n" +
                    "RIGHT-CLICK the old fiber to cut it. Place a Model S ($200) next to the " +
                    "ingress and drag a new link. Full pay needs time-to-first-token under 1.1s.\n\n" +
                    "GOAL: earn $350. Three SLA breaches and Acme walks.",
                StartingCash = 500,
                Palette = new[] { NodeKind.ModelS },
                Clients = new[]
                {
                    new ClientSpec { Name = "Acme", IngressX = 4, IngressY = 13,
                        Ramp = new[] { new RateKey { T = 0, Rate = 0.8f }, new RateKey { T = 240, Rate = 1.2f } },
                        Mix = new[] { 1f, 0f, 0f } },
                },
                PrePlaced = new[]
                {
                    new PrePlacedNode { Kind = NodeKind.ModelL, X = 40, Y = 12, LinkFromClient = 0 },
                },
                SurviveSeconds = 0,
                EarnGoal = 350,
                Seed = 101,
            },
            new ContractSpec
            {
                Number = 2,
                Title = "Two Kinds of Users",
                Brief =
                    "Acme ships a pro tier: ▲ Standard requests (30%) join the stream. " +
                    "Your Model S refuses them — they need a Model M ($600).\n\n" +
                    "Wire the ingress into a Class Switch ($100): it routes each class toward " +
                    "hardware that can serve it.\n\n" +
                    "At minute 5 marketing runs a promo (+0.7 req/s). Watch for amber links — " +
                    "a clogged fiber freezes its pulse.\n\n" +
                    "GOAL: survive 8 minutes. Fewer than 3 breaches.",
                StartingCash = 950,
                Palette = new[] { NodeKind.ModelS, NodeKind.ModelM, NodeKind.ClassSwitch },
                Clients = new[]
                {
                    new ClientSpec { Name = "Acme", IngressX = 4, IngressY = 13,
                        Ramp = new[] {
                            new RateKey { T = 0, Rate = 1.5f },
                            new RateKey { T = 295, Rate = 1.5f },
                            new RateKey { T = 305, Rate = 2.2f } },
                        Mix = new[] { 0.7f, 0.3f, 0f } },
                },
                SurviveSeconds = 480,
                EarnGoal = 0,
                Seed = 202,
            },
            new ContractSpec
            {
                Number = 3,
                Title = "Rush Hour",
                Brief =
                    "Two clients now. Every couple of minutes traffic SPIKES ×2 for 15 " +
                    "seconds — you get a 20-second warning banner.\n\n" +
                    "The Load Balancer ($150) spreads a stream across up to four downstream " +
                    "pools by least queue. Provision ahead: a Model M takes 20s to load weights.\n\n" +
                    "GOAL: survive 3 spikes (about 7½ minutes). Fewer than 3 breaches.",
                StartingCash = 1500,
                Palette = new[] { NodeKind.ModelS, NodeKind.ModelM, NodeKind.ClassSwitch, NodeKind.LoadBalancer },
                Clients = new[]
                {
                    new ClientSpec { Name = "Acme", IngressX = 4, IngressY = 19,
                        Ramp = new[] { new RateKey { T = 0, Rate = 1.3f } },
                        Mix = new[] { 0.65f, 0.35f, 0f } },
                    new ClientSpec { Name = "Borealis", IngressX = 4, IngressY = 7,
                        Ramp = new[] { new RateKey { T = 0, Rate = 1.2f } },
                        Mix = new[] { 0.65f, 0.35f, 0f } },
                },
                Spikes = new SpikeSpec { FirstAt = 180f, Every = 140f, Multiplier = 2f, Duration = 15f },
                SurviveSeconds = 460,
                EarnGoal = 0,
                Seed = 303,
            },
        };
    }
}
