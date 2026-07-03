namespace Throughput.Sim
{
    public sealed class GoalChip
    {
        public string Text;
        public float Reward;
        public bool Done;
        public bool HasProgress;
        public float Progress;    // 0..1 when HasProgress
    }

    /// Sequential goal chips (docs/DESIGN2.md §5.4). Current + next silhouette visible.
    public sealed class GoalChips
    {
        public readonly GoalChip[] Chips =
        {
            new GoalChip { Text = "▦ Deploy a rack" },
            new GoalChip { Text = "▦ Two more racks", Reward = 250 },
            new GoalChip { Text = "❄ Cool the hall", Reward = 250 },
            new GoalChip { Text = "💰 Earn $1,200", HasProgress = true },
            new GoalChip { Text = "📄 Sign a contract" },
            new GoalChip { Text = "⚡ 100 kW IT online", Reward = 1500, HasProgress = true },
            new GoalChip { Text = "⭐ NET green thru a peak", Reward = 500 },
            new GoalChip { Text = "💰 Earn $15,000 lifetime", HasProgress = true },
        };

        public int CurrentIndex { get; private set; }

        public GoalChip Current => CurrentIndex < Chips.Length ? Chips[CurrentIndex] : null;
        public GoalChip Next => CurrentIndex + 1 < Chips.Length ? Chips[CurrentIndex + 1] : null;

        public void Step(DcWorld w)
        {
            // Chip 3 becoming current unlocks the CRAC chip; chip 4 progress gates GPU.
            if (CurrentIndex >= 2) w.CracUnlocked = true;
            if (w.Earned >= Balance.GpuEarnedGate) w.GpuUnlocked = true;

            GoalChip c = Current;
            if (c == null) return;

            bool done = false;
            switch (CurrentIndex)
            {
                case 0: done = w.RackCount >= 1; break;
                case 1: done = w.RackCount >= 3; break;
                case 2: done = w.CracCount >= 1; break;
                case 3:
                    c.Progress = UnityEngine.Mathf.Clamp01(w.Earned / Balance.GpuEarnedGate);
                    done = w.Earned >= Balance.GpuEarnedGate;
                    break;
                case 4: done = w.AnyContractSigned; break;
                case 5:
                    c.Progress = UnityEngine.Mathf.Clamp01(w.OnlineItKw / 100f);
                    done = w.OnlineItKw >= 100f;
                    break;
                case 6: done = w.PeakHeldGreen; break;
                case 7:
                    c.Progress = UnityEngine.Mathf.Clamp01(w.Earned / Balance.LifetimeGate);
                    done = w.Earned >= Balance.LifetimeGate;
                    break;
            }

            if (done)
            {
                c.Done = true;
                if (c.Reward > 0f) w.ReceiveReward(c.Reward);
                w.Ticker($"Goal complete: {c.Text}" + (c.Reward > 0 ? $"  +${c.Reward:0}" : ""), 0);
                w.EmitChime(0);
                CurrentIndex++;
                if (CurrentIndex == Chips.Length)
                    w.Ticker("Demo grid mastered — keep scaling. Phase B hardware incoming.", 0);
            }
        }
    }
}
