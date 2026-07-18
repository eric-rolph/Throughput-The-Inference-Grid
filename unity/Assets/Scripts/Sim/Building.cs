using System.Collections.Generic;

namespace Throughput.Sim
{
    public enum BuildingState
    {
        Booting, Online, HeatShutdown, TrippedDark, NoUplink, ToggledOff,
    }

    public enum Verdict { Green, Amber, Red }

    public struct PlacementCheck
    {
        public Verdict Verdict;
        public string Reason;     // <=8 words, empty when green
        public float Cost;
        public float WalletAfter;
    }

    public sealed class Building
    {
        public int Id;
        public BuildingKind Kind;
        public int X, Y;
        public int PduId = -1;            // powering PDU (-1 = feed-direct)
        public bool PrePlaced;
        public bool Removed;

        public BuildingState State = BuildingState.Booting;
        public float BootRemaining;
        public float DarkRemaining;       // TrippedDark countdown
        public float OverloadTimer;       // PDU only
        public BuildingState StateBeforeTrip;
        public bool HasStateBeforeTrip;
        public bool ToggledOff;
        public bool NoUplinkFlag;
        public bool HasPower;
        public long PlacedTick;

        public float ServedPf;            // current tick's fill
        public float ServedPurplePf;
        public float RevenueRate;         // $/s this tick
        public float TileTemp = Balance.AmbientTemp;
        public float FloaterAccum;        // $ accumulated toward next floater
        public float FloaterClock;

        public BuildingSpec Spec => Balance.Spec(Kind);

        public bool Producing =>
            State == BuildingState.Online && HasPower && !NoUplinkFlag;

        /// Draws grid power (full watts) in every state except off/dark.
        public bool DrawsPower =>
            !ToggledOff && State != BuildingState.TrippedDark && !Removed;

        public float ThrottleMult
        {
            get
            {
                if (TileTemp >= Balance.HotTemp) return Balance.HotThrottle;
                if (TileTemp >= Balance.WarmTemp) return Balance.WarmThrottle;
                return 1f;
            }
        }
    }

    public struct PayoutEvent { public float Amount; public float X, Y; }
    public struct TickerEvent { public string Message; public byte Severity; }
    public struct TripEvent { public float X, Y; }
    public struct ChimeEvent { public byte Kind; } // 0 goal, 1 contract, 2 unlock
}
