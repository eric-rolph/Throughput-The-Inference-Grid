using System.Collections.Generic;

namespace Throughput.Sim
{
    public enum OfferState { Pending, Offered, Active, Fulfilled, Failed, Passed }

    public sealed class Offer
    {
        public string Name, Tag;
        public int NeedsGpuOnline;
        public float Advance, AddsPurplePf, RateBonus, Penalty;
        public int DeadlineDay;
        public OfferState State = OfferState.Pending;
        public int ReofferDay;
        public bool AdvancePaid;

        public bool ContributesDemand => State == OfferState.Active || State == OfferState.Fulfilled;
    }

    /// Hardcoded two-card timeline (docs/DESIGN2.md §5.3). No generator in Phase A.
    public sealed class ContractScript
    {
        public readonly Offer[] Offers =
        {
            new Offer { Name = "PICOCHAT", Tag = "inference", NeedsGpuOnline = 1, Advance = 8000,
                        AddsPurplePf = 4, RateBonus = 1.25f, DeadlineDay = 3, Penalty = 1000 },
            new Offer { Name = "NIMBUS AI", Tag = "training", NeedsGpuOnline = 4, Advance = 8000,
                        AddsPurplePf = 20, RateBonus = 1.5f, DeadlineDay = 7, Penalty = 5000 },
        };

        public bool NimbusFulfilled => Offers[1].State == OfferState.Fulfilled;

        public void Step(DcWorld w)
        {
            Offer pico = Offers[0], nimbus = Offers[1];

            if (pico.State == OfferState.Pending && w.GpuUnlocked)
            { MakeCurrent(pico, w); w.Ticker($"Contract offer: {pico.Name}", 0); }
            if (nimbus.State == OfferState.Pending && w.Day >= 4)
            { MakeCurrent(nimbus, w); w.Ticker($"Contract offer: {nimbus.Name}", 0); }

            foreach (Offer o in Offers)
            {
                if (o.State == OfferState.Offered && w.Day > o.DeadlineDay)
                {
                    o.State = OfferState.Passed;
                    o.ReofferDay = w.Day + 1;
                }

                if ((o.State == OfferState.Passed || o.State == OfferState.Failed) &&
                    w.Day >= o.ReofferDay)
                {
                    MakeCurrent(o, w);
                    w.Ticker($"Contract reoffered: {o.Name}", 0);
                }

                if (o.State == OfferState.Active && w.Day > o.DeadlineDay)
                {
                    o.State = OfferState.Failed;
                    o.ReofferDay = w.Day + 1;
                    w.PayPenalty(o.Penalty);
                    w.Ticker($"{o.Name} deadline missed — penalty ${o.Penalty:0}. Recovery offer arrives next day.", 2);
                }
                else if (o.State == OfferState.Active && w.GpuOnlineCount >= o.NeedsGpuOnline)
                {
                    o.State = OfferState.Fulfilled;
                    w.Ticker($"{o.Name} capacity online — bonus rate flowing", 0);
                    w.EmitChime(1);
                }
            }
        }

        private static void MakeCurrent(Offer o, DcWorld w)
        {
            if (o.DeadlineDay < w.Day) o.DeadlineDay = w.Day + 2;
            o.State = OfferState.Offered;
        }

        public void Accept(int idx, DcWorld w)
        {
            Offer o = Offers[idx];
            if (o.State != OfferState.Offered || w.Day > o.DeadlineDay) return;
            o.State = OfferState.Active;
            if (!o.AdvancePaid)
            {
                o.AdvancePaid = true;
                w.ReceiveAdvance(o.Advance);
                w.Ticker($"{o.Name} signed — advance ${o.Advance:0}", 0);
            }
            else w.Ticker($"{o.Name} recovery contract signed — no second advance", 0);
        }

        public void Pass(int idx, DcWorld w)
        {
            Offer o = Offers[idx];
            if (o.State != OfferState.Offered) return;
            o.State = OfferState.Passed;
            o.ReofferDay = w.Day + 1;
        }

        public float ActivePurplePf()
        {
            float pf = 0f;
            foreach (Offer o in Offers) if (o.ContributesDemand) pf += o.AddsPurplePf;
            return pf;
        }
    }
}
