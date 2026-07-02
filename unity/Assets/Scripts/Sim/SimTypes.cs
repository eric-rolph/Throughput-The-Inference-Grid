using System.Collections.Generic;

namespace Throughput.Sim
{
    public enum PacketState { Free = 0, OnLink, Queued, InService, Backlogged }

    public struct SimPacket
    {
        public PacketState State;
        public PacketClass Class;
        public int ClientId;        // index of spawning ingress node
        public int LinkId;          // when OnLink
        public float LinkDist;      // tiles along link path
        public int NodeId;          // when Queued/InService/Backlogged
        public long SpawnTick;
        public long ServeStartTick; // -1 until service begins
    }

    public sealed class SimNode
    {
        public int Id;
        public NodeKind Kind;
        public int X, Y;                       // anchor tile (bottom-left)
        public bool PrePlaced;                 // authored by the contract map
        public float ColdStartRemaining;       // seconds; >0 = still provisioning
        public readonly List<int> OutLinks = new List<int>();
        public readonly List<int> InLinks = new List<int>();
        public readonly List<int> Queue = new List<int>();       // packet ids
        public readonly List<int> Backlog = new List<int>();     // ingress only
        public float[] SlotRemaining;          // seconds left per busy slot; 0 = free
        public int[] SlotPacket;               // packet id per slot; -1 = free
        public int RoundRobin;                 // switch fairness cursor

        // Ingress client state
        public float SpawnAccumulator;
        public float RingFill;                 // 0..1 error-budget burn
        public bool Churned;
        public float RatingEma = 1f;

        public bool Active => ColdStartRemaining <= 0f;

        public NodeSpec Spec => Tuning.Spec(Kind);

        public int BusySlots
        {
            get
            {
                if (SlotPacket == null) return 0;
                int n = 0;
                for (int i = 0; i < SlotPacket.Length; i++) if (SlotPacket[i] >= 0) n++;
                return n;
            }
        }

        public float CenterX => X + Spec.Size * 0.5f;
        public float CenterY => Y + Spec.Size * 0.5f;
    }

    public sealed class SimLink
    {
        public int Id;
        public int FromNode, ToNode;
        public List<UnityEngine.Vector2> Path = new List<UnityEngine.Vector2>(); // polyline points (world)
        public float Length;                    // tiles
        public float Speed = Tuning.LinkSpeedCopper;
        public readonly List<int> Packets = new List<int>(); // ordered: [0] = closest to target
        public float ClogHeat;                  // 0..1
        public bool Removed;

        public UnityEngine.Vector2 PointAt(float dist)
        {
            if (Path.Count == 0) return UnityEngine.Vector2.zero;
            float remaining = dist;
            for (int i = 0; i < Path.Count - 1; i++)
            {
                UnityEngine.Vector2 a = Path[i], b = Path[i + 1];
                float seg = UnityEngine.Vector2.Distance(a, b);
                if (remaining <= seg || i == Path.Count - 2)
                    return UnityEngine.Vector2.Lerp(a, b, seg <= 0f ? 0f : UnityEngine.Mathf.Clamp01(remaining / seg));
                remaining -= seg;
            }
            return Path[Path.Count - 1];
        }
    }

    // ---- Events the presentation layer consumes (drained each frame) ----

    public struct PayoutEvent
    {
        public float Amount;
        public float Factor;      // 1 = full pay
        public float X, Y;        // world position (server)
        public PacketClass Class;
    }

    public struct TickerEvent
    {
        public string Message;
        public byte Severity;     // 0 info, 1 warn, 2 alert
    }
}
