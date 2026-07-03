using System.Collections.Generic;
using UnityEngine;

namespace Throughput.Sim
{
    /// The whole data-center sim. 20 Hz fixed tick; presentation drains event
    /// lists each frame. Tick order per BUILDSPEC: Clock/Price → Power(trips) →
    /// Heat(2Hz) → Demand/Assign → Revenue/Electricity → Contracts → Goals → Timers.
    public sealed class DcWorld
    {
        public const int GridW = Balance.GridW;
        public const int GridH = Balance.GridH;

        public readonly List<Building> Buildings = new List<Building>();
        public readonly HeatField Heat = new HeatField();
        public readonly ContractScript Contracts = new ContractScript();
        public readonly GoalChips Goals = new GoalChips();

        public readonly List<PayoutEvent> PayoutEvents = new List<PayoutEvent>();
        public readonly List<TickerEvent> TickerEvents = new List<TickerEvent>();
        public readonly List<TripEvent> TripEvents = new List<TripEvent>();
        public readonly List<ChimeEvent> ChimeEvents = new List<ChimeEvent>();

        public long Tick { get; private set; }
        public float Elapsed => Tick * Balance.TickDt;
        public float Cash { get; private set; } = Balance.StartingCash;
        public float Earned { get; private set; }           // revenue + rewards + advances
        public float RevenuePerSec { get; private set; }
        public float PowerCostPerSec { get; private set; }
        public float NetPerSec => RevenuePerSec - PowerCostPerSec;

        public float FeedCapKw { get; private set; } = Balance.FeedCapKw;
        public float FeedLoadKw { get; private set; }
        public float BandwidthCap { get; private set; } = Balance.UplinkGbps;
        public float BandwidthUsed { get; private set; }
        public float SubstationEta { get; private set; } = -1f;

        public float DemandCyanPf { get; private set; }
        public float DemandPurplePf { get; private set; }
        public float ServedPf { get; private set; }
        public float CapacityPf { get; private set; }
        public float QueueDepth { get; private set; }
        public float BreakevenPf { get; private set; }

        public int Day { get; private set; } = 1;
        public float ClockHours { get; private set; } = Balance.StartHour;
        public float PricePerKwS { get; private set; }
        public bool PriceRising { get; private set; }

        public bool CracUnlocked;      // set by GoalChips
        public bool GpuUnlocked;       // set by GoalChips
        public bool PeakHeldGreen { get; private set; }
        public bool AnyContractSigned { get; private set; }

        public int RackCount { get; private set; }
        public int CracCount { get; private set; }
        public int GpuOnlineCount { get; private set; }
        public float OnlineItKw { get; private set; }

        private readonly int[,] _buildingAt = new int[GridW, GridH];
        private bool _heatDirty = true;
        private int _lastPlacedId = -1;
        private float _lastPlacedAt = -999f;
        private bool _peakWindowFailed;
        private bool _inPeakWindow;
        private readonly List<Building> _rackScratch = new List<Building>();

        public DcWorld()
        {
            for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++)
                    _buildingAt[x, y] = -1;

            // Pre-placed per DESIGN2 §3: feed (top), uplink (left), PDU #1.
            Create(BuildingKind.GridFeed, 11, 15, prePlaced: true);
            Create(BuildingKind.Uplink, 1, 8, prePlaced: true);
            Create(BuildingKind.Pdu, 6, 8, prePlaced: true);
            PricePerKwS = Balance.Price(1, Balance.StartHour);
        }

        public void Ticker(string msg, byte severity) =>
            TickerEvents.Add(new TickerEvent { Message = msg, Severity = severity });
        public void EmitChime(byte kind) => ChimeEvents.Add(new ChimeEvent { Kind = kind });
        public void ReceiveAdvance(float amt) { Cash += amt; Earned += amt; AnyContractSigned = true; }
        public void ReceiveReward(float amt) { Cash += amt; Earned += amt; }
        public void PayPenalty(float amt) { Cash -= amt; }

        // ------------------------------------------------------------------ tick

        public void Step()
        {
            Tick++;
            float dt = Balance.TickDt;

            StepClock(dt);
            StepPower(dt);
            if (_heatDirty || Tick % Balance.HeatRebuildTicks == 0)
            {
                Heat.Rebuild(Buildings);
                _heatDirty = false;
                StepHeatStates();
            }
            StepDemand(dt);
            StepMoney(dt);
            Contracts.Step(this);
            Goals.Step(this);
            StepTimers(dt);
        }

        private void StepClock(float dt)
        {
            float prevPrice = PricePerKwS;
            float hoursPerSecond = 24f / Balance.DaySeconds;
            ClockHours += dt * hoursPerSecond;
            if (ClockHours >= 24f) { ClockHours -= 24f; Day++; Ticker($"Day {Day} — demand keeps climbing", 0); }
            PricePerKwS = Balance.Price(Day, ClockHours);
            PriceRising = PricePerKwS >= prevPrice;

            // Chip 7: hold NET green through the 17:00–19:00 peak (day 2+)
            bool inWindow = Day >= 2 && ClockHours >= 17f && ClockHours < 19f;
            if (inWindow && !_inPeakWindow) _peakWindowFailed = false;
            if (inWindow && NetPerSec < 0f) _peakWindowFailed = true;
            if (!inWindow && _inPeakWindow && !_peakWindowFailed) PeakHeldGreen = true;
            _inPeakWindow = inWindow;
        }

        private void StepPower(float dt)
        {
            // Assign every powered non-PDU building to a covering live PDU.
            foreach (Building b in Buildings)
            {
                if (b.Removed || b.Kind == BuildingKind.Pdu ||
                    b.Kind == BuildingKind.GridFeed || b.Kind == BuildingKind.Uplink) continue;
                b.PduId = FindCoveringPdu(b.X, b.Y);
            }

            // Sum loads.
            FeedLoadKw = 0f;
            foreach (Building b in Buildings)
            {
                if (b.Removed || !b.DrawsPower) continue;
                if (b.Kind == BuildingKind.GridFeed || b.State == BuildingState.HeatShutdown) continue;
                bool needsPdu = b.Kind == BuildingKind.CpuRack || b.Kind == BuildingKind.GpuRack ||
                                b.Kind == BuildingKind.Crac;
                if (needsPdu && b.PduId < 0) continue; // unpowered — no draw
                FeedLoadKw += b.Spec.DrawKw;
            }

            // Per-PDU overload → trip.
            foreach (Building pdu in Buildings)
            {
                if (pdu.Removed || pdu.Kind != BuildingKind.Pdu) continue;
                if (pdu.State == BuildingState.TrippedDark || pdu.ToggledOff) { pdu.OverloadTimer = 0f; continue; }

                float load = PduLoad(pdu.Id);
                if (load > pdu.Spec.PduCapKw)
                {
                    pdu.OverloadTimer += dt;
                    if (pdu.OverloadTimer >= Balance.TripSeconds)
                        TripPdu(pdu);
                }
                else pdu.OverloadTimer = 0f;
            }
        }

        public float PduLoad(int pduId)
        {
            float load = 0f;
            foreach (Building b in Buildings)
                if (!b.Removed && b.PduId == pduId && b.DrawsPower &&
                    b.State != BuildingState.HeatShutdown)
                    load += b.Spec.DrawKw;
            return load;
        }

        private void TripPdu(Building pdu)
        {
            pdu.State = BuildingState.TrippedDark;
            pdu.DarkRemaining = Balance.DarkSeconds;
            pdu.OverloadTimer = 0f;
            int stage = 0;
            foreach (Building b in Buildings)
            {
                if (b.Removed || b.PduId != pdu.Id) continue;
                b.State = BuildingState.TrippedDark;
                b.DarkRemaining = Balance.DarkSeconds + Balance.RebootStagger * (++stage);
            }
            _heatDirty = true;
            TripEvents.Add(new TripEvent { X = pdu.X + 0.5f, Y = pdu.Y + 0.5f });
            Ticker("BREAKER TRIP — PDU overloaded. Subtree dark 8s. Spread the load.", 2);
        }

        private int FindCoveringPdu(int x, int y)
        {
            int best = -1;
            float bestD = float.MaxValue;
            foreach (Building p in Buildings)
            {
                if (p.Removed || p.Kind != BuildingKind.Pdu) continue;
                if (p.ToggledOff) continue;
                float d = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(x, y));
                if (d <= p.Spec.Radius && d < bestD) { bestD = d; best = p.Id; }
            }
            return best;
        }

        private void StepHeatStates()
        {
            foreach (Building b in Buildings)
            {
                if (b.Removed || !b.Spec.IsRack) continue;
                if (b.State == BuildingState.Online && b.TileTemp >= Balance.CriticalTemp)
                {
                    b.State = BuildingState.HeatShutdown;
                    _heatDirty = true;
                    Ticker($"{b.Spec.Name} thermal shutdown at {b.TileTemp:0}° — cool it, then restart ($400)", 2);
                }
            }
        }

        private void StepDemand(float dt)
        {
            float elapsedDays = (Day - 1) + ClockHoursSinceStart() / 24f;
            float ambient = Balance.DemandTotalPf(elapsedDays);
            float share = Balance.PurpleShare(Day);
            DemandCyanPf = ambient * (1f - share);
            DemandPurplePf = ambient * share + Contracts.ActivePurplePf();

            // Bandwidth: newest racks over cap get NO UPLINK.
            _rackScratch.Clear();
            foreach (Building b in Buildings)
                if (!b.Removed && b.Spec.IsRack && b.State == BuildingState.Online && !b.ToggledOff)
                    _rackScratch.Add(b);
            _rackScratch.Sort((a, b) => a.PlacedTick.CompareTo(b.PlacedTick));

            float bw = 0f;
            BandwidthUsed = 0f;
            foreach (Building b in _rackScratch)
            {
                bw += b.Spec.BandwidthGbps;
                b.NoUplinkFlag = bw > BandwidthCap;
                if (!b.NoUplinkFlag) BandwidthUsed += b.Spec.BandwidthGbps;
            }

            // Per-rack fill, oldest-first: contract purple → ambient purple (GPU) → cyan (all).
            CapacityPf = 0f;
            foreach (Building b in _rackScratch)
            {
                b.ServedPf = 0f; b.ServedPurplePf = 0f; b.RevenueRate = 0f;
                if (b.Producing) CapacityPf += b.Spec.ComputePf * b.ThrottleMult;
            }

            float contractPurple = Contracts.ActivePurplePf();
            float contractBonus = ContractBlendedBonus();
            float ambientPurple = Mathf.Max(0f, DemandPurplePf - contractPurple);
            float cyan = DemandCyanPf;
            ServedPf = 0f;

            foreach (Building b in _rackScratch)   // GPU pass: purple pools
            {
                if (!b.Producing || !b.Spec.ServesPurple) continue;
                float free = b.Spec.ComputePf * b.ThrottleMult;
                float perPf = b.Spec.RevenuePerSec / b.Spec.ComputePf;

                float take = Mathf.Min(free, contractPurple);
                contractPurple -= take; free -= take;
                b.RevenueRate += take * perPf * contractBonus;
                b.ServedPurplePf += take;

                float take2 = Mathf.Min(free, ambientPurple);
                ambientPurple -= take2; free -= take2;
                b.RevenueRate += take2 * perPf;
                b.ServedPurplePf += take2;

                b.ServedPf = b.Spec.ComputePf * b.ThrottleMult - free;
                ServedPf += b.ServedPf;
            }
            foreach (Building b in _rackScratch)   // cyan pass: all racks
            {
                if (!b.Producing) continue;
                float free = b.Spec.ComputePf * b.ThrottleMult - b.ServedPf;
                if (free <= 0f) continue;
                float perPf = b.Spec.RevenuePerSec / b.Spec.ComputePf;
                float take = Mathf.Min(free, cyan);
                cyan -= take;
                b.RevenueRate += take * perPf;
                b.ServedPf += take;
                ServedPf += take;
            }

            QueueDepth = Mathf.Max(0f, DemandCyanPf + DemandPurplePf - ServedPf);
        }

        private float ContractBlendedBonus()
        {
            float pf = 0f, weighted = 0f;
            foreach (Offer o in Contracts.Offers)
                if (o.ContributesDemand) { pf += o.AddsPurplePf; weighted += o.AddsPurplePf * o.RateBonus; }
            return pf > 0f ? weighted / pf : 1f;
        }

        private float ClockHoursSinceStart()
        {
            float h = ClockHours - Balance.StartHour;
            return h < 0 ? h + 24f : h;
        }

        private void StepMoney(float dt)
        {
            RevenuePerSec = 0f;
            RackCount = 0; CracCount = 0; GpuOnlineCount = 0; OnlineItKw = 0f;

            foreach (Building b in Buildings)
            {
                if (b.Removed) continue;
                if (b.Spec.IsRack)
                {
                    RackCount++;
                    if (b.State == BuildingState.Online && !b.ToggledOff)
                    {
                        OnlineItKw += b.Spec.DrawKw;
                        if (b.Kind == BuildingKind.GpuRack) GpuOnlineCount++;
                    }
                }
                if (b.Kind == BuildingKind.Crac) CracCount++;

                RevenuePerSec += b.RevenueRate;

                // $ floaters: one per rack per second, aggregated
                if (b.RevenueRate > 0f)
                {
                    b.FloaterAccum += b.RevenueRate * dt;
                    b.FloaterClock += dt;
                    if (b.FloaterClock >= 1f && b.FloaterAccum >= 0.5f)
                    {
                        PayoutEvents.Add(new PayoutEvent { Amount = b.FloaterAccum, X = b.X + 0.5f, Y = b.Y + 1.1f });
                        b.FloaterAccum = 0f; b.FloaterClock = 0f;
                    }
                }
            }

            PowerCostPerSec = FeedLoadKw * PricePerKwS;
            Cash += (RevenuePerSec - PowerCostPerSec) * dt;
            Earned += RevenuePerSec * dt;

            // Breakeven marker: served PF where revenue covers current electricity.
            float perPf = 0f, cap = 0f;
            foreach (Building b in _rackScratch)
            {
                if (!b.Producing) continue;
                perPf += b.Spec.RevenuePerSec;
                cap += b.Spec.ComputePf;
            }
            float blended = cap > 0f ? perPf / cap : 1.5f;
            BreakevenPf = blended > 0f ? PowerCostPerSec / blended : 0f;
        }

        private void StepTimers(float dt)
        {
            if (SubstationEta > 0f)
            {
                SubstationEta -= dt;
                if (SubstationEta <= 0f)
                {
                    SubstationEta = -1f;
                    FeedCapKw += Balance.SubstationKw;
                    Ticker($"Substation energized — feed capacity now {FeedCapKw:0} kW", 0);
                    EmitChime(2);
                }
            }

            foreach (Building b in Buildings)
            {
                if (b.Removed) continue;
                if (b.State == BuildingState.TrippedDark)
                {
                    b.DarkRemaining -= dt;
                    if (b.DarkRemaining <= 0f)
                    {
                        b.State = BuildingState.Booting;
                        b.BootRemaining = 0.5f;
                        _heatDirty = true;
                    }
                }
                else if (b.State == BuildingState.Booting)
                {
                    b.BootRemaining -= dt;
                    if (b.BootRemaining <= 0f)
                    {
                        b.State = BuildingState.Online;
                        _heatDirty = true;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ commands

        public PlacementCheck CheckPlace(BuildingKind k, int x, int y)
        {
            BuildingSpec spec = Balance.Spec(k);
            var chk = new PlacementCheck { Verdict = Verdict.Green, Reason = "", Cost = spec.Cost, WalletAfter = Cash - spec.Cost };

            if (x < 0 || y < 0 || x >= GridW || y >= GridH || _buildingAt[x, y] >= 0)
            { chk.Verdict = Verdict.Red; chk.Reason = "Tile occupied"; return chk; }
            if (Cash < spec.Cost)
            { chk.Verdict = Verdict.Red; chk.Reason = "Not enough cash"; return chk; }
            if (FeedLoadKw + spec.DrawKw > FeedCapKw)
            { chk.Verdict = Verdict.Red; chk.Reason = "Grid feed maxed — order substation"; return chk; }

            bool needsRing = k == BuildingKind.CpuRack || k == BuildingKind.GpuRack || k == BuildingKind.Crac;
            int pduId = FindCoveringPdu(x, y);
            if (needsRing && pduId < 0)
            { chk.Verdict = Verdict.Red; chk.Reason = "Needs power — build in a ring"; return chk; }

            // Amber warnings (placement allowed, consequences yours)
            if (needsRing)
            {
                float load = PduLoad(pduId) + spec.DrawKw;
                float cap = Buildings[pduId].Spec.PduCapKw;
                if (load > cap)
                {
                    chk.Verdict = Verdict.Amber;
                    chk.Reason = $"PDU at {load / cap * 100f:0}% — breaker will trip";
                    return chk;
                }
            }
            if (spec.IsRack && Heat.At(x, y) >= Balance.WarmTemp)
            { chk.Verdict = Verdict.Amber; chk.Reason = "Too hot here — will throttle"; return chk; }
            if (spec.IsRack && BandwidthUsed + spec.BandwidthGbps > BandwidthCap)
            { chk.Verdict = Verdict.Amber; chk.Reason = "Uplink saturated — rack will idle"; return chk; }

            return chk;
        }

        public Building TryPlace(BuildingKind k, int x, int y)
        {
            PlacementCheck chk = CheckPlace(k, x, y);
            if (chk.Verdict == Verdict.Red) return null;
            Cash -= chk.Cost;
            Building b = Create(k, x, y, prePlaced: false);
            b.State = BuildingState.Booting;
            b.BootRemaining = b.Spec.BootSeconds;
            _lastPlacedId = b.Id;
            _lastPlacedAt = Elapsed;
            _heatDirty = true;
            return b;
        }

        private Building Create(BuildingKind k, int x, int y, bool prePlaced)
        {
            var b = new Building
            {
                Id = Buildings.Count, Kind = k, X = x, Y = y, PrePlaced = prePlaced,
                State = prePlaced ? BuildingState.Online : BuildingState.Booting,
                PlacedTick = Tick,
            };
            Buildings.Add(b);
            _buildingAt[x, y] = b.Id;
            return b;
        }

        public bool TrySell(int id)
        {
            Building b = Buildings[id];
            if (b.Removed || b.PrePlaced) return false;
            float frac = Elapsed - _lastPlacedAt <= Balance.SellFullRefundSeconds && id == _lastPlacedId
                ? 1f : Balance.SellRefundFraction;
            Cash += b.Spec.Cost * frac;
            Remove(b);
            Ticker($"{b.Spec.Name} sold (+${b.Spec.Cost * frac:0})", 0);
            return true;
        }

        public bool CanUndo =>
            _lastPlacedId >= 0 && Elapsed - _lastPlacedAt <= Balance.UndoWindowSeconds &&
            _lastPlacedId < Buildings.Count && !Buildings[_lastPlacedId].Removed;

        public bool TryUndo()
        {
            if (!CanUndo) return false;
            Building b = Buildings[_lastPlacedId];
            Cash += b.Spec.Cost;
            Remove(b);
            _lastPlacedId = -1;
            Ticker("Placement undone — full refund", 0);
            return true;
        }

        private void Remove(Building b)
        {
            b.Removed = true;
            _buildingAt[b.X, b.Y] = -1;
            _heatDirty = true;
        }

        public void ToggleBuilding(int id)
        {
            Building b = Buildings[id];
            if (b.Removed || b.Kind == BuildingKind.GridFeed || b.Kind == BuildingKind.Uplink) return;
            b.ToggledOff = !b.ToggledOff;
            _heatDirty = true;
            Ticker($"{b.Spec.Name} {(b.ToggledOff ? "powered down" : "powered up")}", 0);
        }

        public bool BuyUplink()
        {
            if (Cash < Balance.UplinkUpgradeCost) return false;
            Cash -= Balance.UplinkUpgradeCost;
            BandwidthCap += Balance.UplinkUpgradeGbps;
            Ticker($"Uplink upgraded — {BandwidthCap:0} Gbps", 0);
            EmitChime(2);
            return true;
        }

        public bool OrderSubstation()
        {
            if (Cash < Balance.SubstationCost || SubstationEta > 0f) return false;
            Cash -= Balance.SubstationCost;
            SubstationEta = Balance.SubstationLeadSeconds;
            Ticker($"Substation ordered — arrives in {Balance.SubstationLeadSeconds:0}s", 0);
            return true;
        }

        public bool RestartHeatShutdown(int id)
        {
            Building b = Buildings[id];
            if (b.Removed || b.State != BuildingState.HeatShutdown) return false;
            if (b.TileTemp >= Balance.HotTemp) { Ticker($"Still {b.TileTemp:0}° — cool below 60° first", 1); return false; }
            if (Cash < Balance.HeatRestartCost) return false;
            Cash -= Balance.HeatRestartCost;
            b.State = BuildingState.Booting;
            b.BootRemaining = 1f;
            _heatDirty = true;
            return true;
        }

        public int BuildingIdAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= GridW || y >= GridH) return -1;
            return _buildingAt[x, y];
        }
    }
}
