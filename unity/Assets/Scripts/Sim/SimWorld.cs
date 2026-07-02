using System.Collections.Generic;
using UnityEngine;

namespace Throughput.Sim
{
    public enum ContractState { Running, Won, Failed }

    /// The whole deterministic game state. 20 Hz fixed tick, no UnityEngine
    /// behaviour — presentation reads state and drains event lists each frame.
    public sealed class SimWorld
    {
        public const int GridW = 48;
        public const int GridH = 27;
        private const int MaxPackets = 4096;

        public readonly ContractSpec Contract;
        public readonly List<SimNode> Nodes = new List<SimNode>();
        public readonly List<SimLink> Links = new List<SimLink>();
        public readonly SimPacket[] Packets = new SimPacket[MaxPackets];

        public readonly List<PayoutEvent> PayoutEvents = new List<PayoutEvent>();
        public readonly List<TickerEvent> TickerEvents = new List<TickerEvent>();

        public long Tick { get; private set; }
        public float Time => Tick * Tuning.TickDt;
        public float Cash { get; private set; }
        public float Earned { get; private set; }
        public int Breaches { get; private set; }
        public int Served { get; private set; }
        public int ServedInSlo { get; private set; }
        public int Lost { get; private set; }
        public ContractState State { get; private set; } = ContractState.Running;

        // Spike machinery
        public bool SpikeWarning { get; private set; }
        public bool SpikeActive { get; private set; }
        public int SpikesSurvived { get; private set; }
        public float SpikeCountdown { get; private set; }  // seconds to next spike (during warning)
        private float _nextSpikeAt;

        private readonly XorShift128 _rng;
        private readonly Stack<int> _freePackets = new Stack<int>();
        private readonly int[,] _nodeAt = new int[GridW, GridH]; // -1 = empty

        public SimWorld(ContractSpec contract)
        {
            Contract = contract;
            Cash = contract.StartingCash;
            _rng = new XorShift128(contract.Seed);
            for (int i = MaxPackets - 1; i >= 0; i--) _freePackets.Push(i);
            for (int x = 0; x < GridW; x++) for (int y = 0; y < GridH; y++) _nodeAt[x, y] = -1;

            foreach (ClientSpec c in contract.Clients)
            {
                SimNode ing = CreateNode(NodeKind.Ingress, c.IngressX, c.IngressY, prePlaced: true);
                ing.ColdStartRemaining = 0f;
            }
            foreach (PrePlacedNode p in contract.PrePlaced)
            {
                SimNode n = CreateNode(p.Kind, p.X, p.Y, prePlaced: true);
                n.ColdStartRemaining = 0f;
                if (p.LinkFromClient >= 0)
                    CreateLink(Nodes[p.LinkFromClient].Id, n.Id, charge: false);
            }
            if (contract.Spikes != null) _nextSpikeAt = contract.Spikes.FirstAt;
        }

        // ------------------------------------------------------------------ tick

        public void Step()
        {
            if (State != ContractState.Running) return;
            Tick++;
            float dt = Tuning.TickDt;

            TickSpikes(dt);
            TickSpawns(dt);
            TickColdStarts(dt);
            TickLinks(dt);
            TickLogicNodes();
            TickServers(dt);
            TickRings(dt);
            TickEconomy(dt);
            EvaluateContract();
        }

        private void TickSpikes(float dt)
        {
            SpikeSpec s = Contract.Spikes;
            if (s == null) return;
            float t = Time;
            SpikeWarning = !SpikeActive && t >= _nextSpikeAt - s.WarningLead && t < _nextSpikeAt;
            SpikeCountdown = Mathf.Max(0f, _nextSpikeAt - t);
            if (!SpikeActive && t >= _nextSpikeAt)
            {
                SpikeActive = true;
                Ticker("TRAFFIC SPIKE — viral moment in progress", 2);
            }
            if (SpikeActive && t >= _nextSpikeAt + s.Duration)
            {
                SpikeActive = false;
                SpikesSurvived++;
                _nextSpikeAt += s.Every;
                Ticker($"Spike absorbed ({SpikesSurvived} survived)", 0);
            }
        }

        private void TickSpawns(float dt)
        {
            float mult = SpikeActive ? Contract.Spikes.Multiplier : 1f;
            for (int ci = 0; ci < Contract.Clients.Length; ci++)
            {
                ClientSpec client = Contract.Clients[ci];
                SimNode ingress = Nodes[ci];
                if (ingress.Churned) continue;
                ingress.SpawnAccumulator += client.RateAt(Time) * mult * dt;
                while (ingress.SpawnAccumulator >= 1f)
                {
                    ingress.SpawnAccumulator -= 1f;
                    SpawnPacket(ingress, RollClass(client));
                }
                DrainBacklog(ingress);
            }
        }

        private PacketClass RollClass(ClientSpec client)
        {
            float r = _rng.NextFloat();
            if (r < client.Mix[0]) return PacketClass.Trivial;
            if (r < client.Mix[0] + client.Mix[1]) return PacketClass.Standard;
            return PacketClass.Complex;
        }

        private void SpawnPacket(SimNode ingress, PacketClass cls)
        {
            if (_freePackets.Count == 0) return;
            int id = _freePackets.Pop();
            Packets[id] = new SimPacket
            {
                State = PacketState.Backlogged,
                Class = cls,
                ClientId = ingress.Id,
                NodeId = ingress.Id,
                SpawnTick = Tick,
                ServeStartTick = -1,
            };
            ingress.Backlog.Add(id);
        }

        private void DrainBacklog(SimNode ingress)
        {
            while (ingress.Backlog.Count > 0)
            {
                if (ingress.OutLinks.Count == 0) return;
                SimLink link = Links[ingress.OutLinks[0]];
                if (!CanEnterLink(link)) return;
                int pid = ingress.Backlog[0];
                ingress.Backlog.RemoveAt(0);
                EnterLink(link, pid);
            }
        }

        private void TickColdStarts(float dt)
        {
            foreach (SimNode n in Nodes)
            {
                if (n.ColdStartRemaining > 0f)
                {
                    n.ColdStartRemaining -= dt;
                    if (n.ColdStartRemaining <= 0f)
                    {
                        n.ColdStartRemaining = 0f;
                        Ticker($"{n.Spec.Name} online", 0);
                    }
                }
            }
        }

        private void TickLinks(float dt)
        {
            foreach (SimLink link in Links)
            {
                if (link.Removed) continue;
                bool blocked = false;

                for (int i = 0; i < link.Packets.Count; i++)
                {
                    int pid = link.Packets[i];
                    float maxDist = i == 0
                        ? link.Length
                        : Packets[link.Packets[i - 1]].LinkDist - Tuning.LinkSpacing;
                    float d = Mathf.Min(Packets[pid].LinkDist + link.Speed * dt, maxDist);
                    Packets[pid].LinkDist = Mathf.Max(Packets[pid].LinkDist, d);
                }

                // Head arrival
                while (link.Packets.Count > 0)
                {
                    int head = link.Packets[0];
                    if (Packets[head].LinkDist < link.Length - 0.001f) break;
                    SimNode target = Nodes[link.ToNode];
                    if (AcceptPacket(target, Packets[head].Class))
                    {
                        link.Packets.RemoveAt(0);
                        Packets[head].State = PacketState.Queued;
                        Packets[head].NodeId = target.Id;
                        target.Queue.Add(head);
                    }
                    else { blocked = true; break; }
                }

                // Entry congestion also heats the link
                if (!blocked && link.Packets.Count > 0)
                {
                    int last = link.Packets[link.Packets.Count - 1];
                    if (Packets[last].LinkDist < Tuning.LinkSpacing) blocked = true;
                }

                link.ClogHeat = Mathf.Clamp01(link.ClogHeat +
                    (blocked ? dt / Tuning.ClogHeatRampSeconds : -dt / Tuning.ClogHeatRampSeconds));
            }
        }

        private bool AcceptPacket(SimNode node, PacketClass cls)
        {
            if (!node.Active) return false;
            NodeSpec spec = node.Spec;
            if (spec.IsServer)
                return spec.CanServe(cls) && node.Queue.Count < spec.QueueDepth;
            if (spec.Slots == 0 && spec.MaxOutLinks > 0)   // logic node
                return node.Queue.Count < spec.QueueDepth;
            return false;
        }

        private void TickLogicNodes()
        {
            foreach (SimNode n in Nodes)
            {
                NodeSpec spec = n.Spec;
                if (spec.IsServer || n.Kind == NodeKind.Ingress || !n.Active) continue;

                for (int qi = 0; qi < n.Queue.Count;)
                {
                    int pid = n.Queue[qi];
                    int linkId = ChooseOutLink(n, Packets[pid].Class);
                    if (linkId >= 0)
                    {
                        n.Queue.RemoveAt(qi);
                        EnterLink(Links[linkId], pid);
                    }
                    else qi++;
                }
            }
        }

        /// Route selection: class-capable targets first; LB = least loaded, Switch = round-robin.
        private int ChooseOutLink(SimNode n, PacketClass cls)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            int count = n.OutLinks.Count;
            for (int k = 0; k < count; k++)
            {
                int idx = (k + n.RoundRobin) % count;
                SimLink link = Links[n.OutLinks[idx]];
                if (link.Removed || !CanEnterLink(link)) continue;
                SimNode target = Nodes[link.ToNode];
                NodeSpec ts = target.Spec;
                bool capable = ts.IsServer ? ts.CanServe(cls) : ts.MaxOutLinks > 0;
                if (!capable) continue;

                float score;
                if (n.Kind == NodeKind.LoadBalancer)
                    score = target.Queue.Count + target.BusySlots + link.Packets.Count * 0.1f;
                else
                    score = k; // round-robin order
                if (score < bestScore) { bestScore = score; best = n.OutLinks[idx]; }
            }
            if (best >= 0 && n.Kind == NodeKind.ClassSwitch) n.RoundRobin++;
            return best;
        }

        private bool CanEnterLink(SimLink link)
        {
            if (link.Removed) return false;
            if (link.Packets.Count == 0) return true;
            return Packets[link.Packets[link.Packets.Count - 1]].LinkDist >= Tuning.LinkSpacing;
        }

        private void EnterLink(SimLink link, int pid)
        {
            Packets[pid].State = PacketState.OnLink;
            Packets[pid].LinkId = link.Id;
            Packets[pid].LinkDist = 0f;
            link.Packets.Add(pid);
        }

        private void TickServers(float dt)
        {
            foreach (SimNode n in Nodes)
            {
                NodeSpec spec = n.Spec;
                if (!spec.IsServer || !n.Active) continue;

                // Complete running services
                for (int s = 0; s < spec.Slots; s++)
                {
                    if (n.SlotPacket[s] < 0) continue;
                    n.SlotRemaining[s] -= dt;
                    if (n.SlotRemaining[s] <= 0f)
                        CompleteService(n, s);
                }

                // Fill free slots from queue
                for (int s = 0; s < spec.Slots && n.Queue.Count > 0; s++)
                {
                    if (n.SlotPacket[s] >= 0) continue;
                    int pid = n.Queue[0];
                    n.Queue.RemoveAt(0);
                    int occ = n.BusySlots + 1;
                    float service = spec.ServiceSeconds(Packets[pid].Class) *
                                    (1f + Tuning.BatchOccupancyPenalty * (occ - 1));
                    n.SlotPacket[s] = pid;
                    n.SlotRemaining[s] = service;
                    Packets[pid].State = PacketState.InService;
                    Packets[pid].ServeStartTick = Tick;
                }
            }
        }

        private void CompleteService(SimNode server, int slot)
        {
            int pid = server.SlotPacket[slot];
            server.SlotPacket[slot] = -1;
            server.SlotRemaining[slot] = 0f;

            SimPacket p = Packets[pid];
            ClassSpec cls = Tuning.Classes[(int)p.Class];
            long measured = cls.MeasureAtServeStart ? p.ServeStartTick : Tick;
            float latency = (measured - p.SpawnTick) * Tuning.TickDt;
            float factor = Tuning.TimeFactor(p.Class, latency);
            float pay = cls.BasePay * factor;

            Cash += pay;
            Earned += pay;
            Served++;
            bool inSlo = factor >= 0.999f;
            if (inSlo) ServedInSlo++;

            SimNode client = Nodes[p.ClientId];
            client.RatingEma = Mathf.Lerp(client.RatingEma, inSlo ? 1f : 0f, 0.03f);

            PayoutEvents.Add(new PayoutEvent
            {
                Amount = pay, Factor = factor,
                X = server.CenterX, Y = server.CenterY, Class = p.Class,
            });

            FreePacket(pid);
        }

        private void TickRings(float dt)
        {
            for (int ci = 0; ci < Contract.Clients.Length; ci++)
            {
                SimNode ing = Nodes[ci];
                if (ing.Churned) continue;
                bool burning = ing.Backlog.Count >= Tuning.IngressBacklogRingThreshold;
                ing.RingFill = Mathf.Clamp01(ing.RingFill +
                    (burning ? dt / Tuning.RingFillSeconds : -dt / Tuning.RingDrainSeconds));
                if (ing.RingFill >= 1f)
                {
                    ing.RingFill = 0f;
                    Breaches++;
                    Cash -= 100f;
                    // Drop the flood so recovery is possible
                    foreach (int pid in ing.Backlog) { FreePacket(pid); Lost++; }
                    ing.Backlog.Clear();
                    Ticker($"SLA BREACH — {Contract.Clients[ci].Name} error budget exhausted (−$100)", 2);
                }
            }
        }

        private void TickEconomy(float dt)
        {
            float upkeepPerSecond = 0f;
            foreach (SimNode n in Nodes)
            {
                if (n.Churned) continue; // removed hardware / churned client
                if (!n.PrePlaced || n.Kind != NodeKind.Ingress)
                    upkeepPerSecond += n.Spec.UpkeepPerMin / 60f;
            }
            Cash -= upkeepPerSecond * dt;
        }

        private void EvaluateContract()
        {
            if (Breaches >= Tuning.BreachesToFail)
            {
                State = ContractState.Failed;
                Ticker("CONTRACT FAILED — too many SLA breaches", 2);
                return;
            }
            bool timeOk = Contract.SurviveSeconds <= 0f || Time >= Contract.SurviveSeconds;
            bool earnOk = Contract.EarnGoal <= 0f || Earned >= Contract.EarnGoal;
            if (timeOk && earnOk && (Contract.SurviveSeconds > 0f || Contract.EarnGoal > 0f))
            {
                State = ContractState.Won;
                Ticker("CONTRACT COMPLETE", 0);
            }
        }

        // ------------------------------------------------------------------ commands

        public bool CanPlace(NodeKind kind, int x, int y)
        {
            NodeSpec spec = Tuning.Spec(kind);
            if (Cash < spec.Cost) return false;
            if (x < 0 || y < 0 || x + spec.Size > GridW || y + spec.Size > GridH) return false;
            for (int dx = 0; dx < spec.Size; dx++)
                for (int dy = 0; dy < spec.Size; dy++)
                    if (_nodeAt[x + dx, y + dy] >= 0) return false;
            return true;
        }

        public SimNode TryPlaceNode(NodeKind kind, int x, int y)
        {
            if (!CanPlace(kind, x, y)) return null;
            NodeSpec spec = Tuning.Spec(kind);
            Cash -= spec.Cost;
            SimNode n = CreateNode(kind, x, y, prePlaced: false);
            n.ColdStartRemaining = spec.ColdStartSeconds;
            Ticker($"{spec.Name} deploying — {spec.ColdStartSeconds:0}s cold start", 0);
            return n;
        }

        private SimNode CreateNode(NodeKind kind, int x, int y, bool prePlaced)
        {
            NodeSpec spec = Tuning.Spec(kind);
            SimNode n = new SimNode { Id = Nodes.Count, Kind = kind, X = x, Y = y, PrePlaced = prePlaced };
            if (spec.Slots > 0)
            {
                n.SlotRemaining = new float[spec.Slots];
                n.SlotPacket = new int[spec.Slots];
                for (int i = 0; i < spec.Slots; i++) n.SlotPacket[i] = -1;
            }
            Nodes.Add(n);
            for (int dx = 0; dx < spec.Size; dx++)
                for (int dy = 0; dy < spec.Size; dy++)
                    _nodeAt[x + dx, y + dy] = n.Id;
            return n;
        }

        public float LinkCost(SimNode from, SimNode to)
        {
            float dist = Mathf.Abs(from.CenterX - to.CenterX) + Mathf.Abs(from.CenterY - to.CenterY);
            return Mathf.Ceil(dist) * Tuning.LinkCostPerTile;
        }

        public SimLink TryCreateLink(int fromId, int toId)
        {
            SimNode from = Nodes[fromId], to = Nodes[toId];
            if (fromId == toId) return null;
            if (from.Spec.MaxOutLinks == 0) return null;
            int liveOut = 0;
            foreach (int l in from.OutLinks) if (!Links[l].Removed) liveOut++;
            if (liveOut >= from.Spec.MaxOutLinks) return null;
            foreach (int l in from.OutLinks)
                if (!Links[l].Removed && Links[l].ToNode == toId) return null; // duplicate
            if (to.Kind == NodeKind.Ingress) return null;
            float cost = LinkCost(from, to);
            if (Cash < cost) return null;
            Cash -= cost;
            return CreateLink(fromId, toId, charge: false);
        }

        private SimLink CreateLink(int fromId, int toId, bool charge)
        {
            SimNode from = Nodes[fromId], to = Nodes[toId];
            SimLink link = new SimLink { Id = Links.Count, FromNode = fromId, ToNode = toId };
            Vector2 a = new Vector2(from.CenterX, from.CenterY);
            Vector2 b = new Vector2(to.CenterX, to.CenterY);
            Vector2 corner = new Vector2(b.x, a.y);
            link.Path.Add(a);
            if (Mathf.Abs(a.y - b.y) > 0.01f && Mathf.Abs(a.x - b.x) > 0.01f) link.Path.Add(corner);
            link.Path.Add(b);
            float len = 0f;
            for (int i = 0; i < link.Path.Count - 1; i++)
                len += Vector2.Distance(link.Path[i], link.Path[i + 1]);
            link.Length = Mathf.Max(len, 0.5f);
            Links.Add(link);
            from.OutLinks.Add(link.Id);
            to.InLinks.Add(link.Id);
            return link;
        }

        public void RemoveLink(int linkId)
        {
            SimLink link = Links[linkId];
            if (link.Removed) return;
            link.Removed = true;
            foreach (int pid in link.Packets) { FreePacket(pid); Lost++; }
            link.Packets.Clear();
            Nodes[link.FromNode].OutLinks.Remove(linkId);
            Nodes[link.ToNode].InLinks.Remove(linkId);
            Ticker("Fiber disconnected", 1);
        }

        public bool TryRemoveNode(int nodeId)
        {
            SimNode n = Nodes[nodeId];
            if (n.Kind == NodeKind.Ingress) return false;
            // Refund only player-placed hardware
            if (!n.PrePlaced) Cash += n.Spec.Cost * Tuning.RefundFraction;
            foreach (int l in new List<int>(n.OutLinks)) RemoveLink(l);
            foreach (int l in new List<int>(n.InLinks)) RemoveLink(l);
            foreach (int pid in n.Queue) { FreePacket(pid); Lost++; }
            n.Queue.Clear();
            if (n.SlotPacket != null)
                for (int s = 0; s < n.SlotPacket.Length; s++)
                    if (n.SlotPacket[s] >= 0) { FreePacket(n.SlotPacket[s]); Lost++; n.SlotPacket[s] = -1; }
            NodeSpec spec = n.Spec;
            for (int dx = 0; dx < spec.Size; dx++)
                for (int dy = 0; dy < spec.Size; dy++)
                    _nodeAt[n.X + dx, n.Y + dy] = -1;
            n.ColdStartRemaining = float.MaxValue; // permanently inert
            n.Churned = true;                      // reused as "removed" flag for non-ingress
            Ticker($"{spec.Name} decommissioned (+${spec.Cost * Tuning.RefundFraction:0})", 0);
            return true;
        }

        public int NodeIdAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= GridW || y >= GridH) return -1;
            return _nodeAt[x, y];
        }

        private void FreePacket(int pid)
        {
            Packets[pid].State = PacketState.Free;
            _freePackets.Push(pid);
        }

        private void Ticker(string msg, byte severity) =>
            TickerEvents.Add(new TickerEvent { Message = msg, Severity = severity });

        public float InSloShare => Served > 0 ? (float)ServedInSlo / Served : 1f;
    }
}
