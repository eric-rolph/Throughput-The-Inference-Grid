using System.Collections.Generic;
using UnityEngine;

namespace Throughput.Sim
{
    /// Stamped radial heat field — no diffusion (docs/DESIGN2.md §4.2).
    /// tileTemp = ambient + Σ heat stamps − Σ cooling stamps, clamped to ambient.
    public sealed class HeatField
    {
        public readonly float[,] Temp = new float[Balance.GridW, Balance.GridH];

        public void Rebuild(List<Building> buildings)
        {
            for (int x = 0; x < Balance.GridW; x++)
                for (int y = 0; y < Balance.GridH; y++)
                    Temp[x, y] = Balance.AmbientTemp;

            foreach (Building b in buildings)
            {
                if (b.Removed || !b.HasPower) continue;
                BuildingSpec spec = b.Spec;

                if (spec.HeatKw > 0f && b.State == BuildingState.Online)
                {
                    float amp = spec.HeatKw * Balance.DegPerHeatKw;
                    if (b.TileTemp >= Balance.HotTemp) amp *= Balance.RunawayMult; // thermal runaway
                    Stamp(b.X, b.Y, amp, Balance.HeatRadius, +1f);
                }
                if (spec.CoolKw > 0f && b.State == BuildingState.Online)
                {
                    Stamp(b.X, b.Y, spec.CoolKw * Balance.DegPerCoolKw, spec.Radius, -1f);
                }
            }

            for (int x = 0; x < Balance.GridW; x++)
                for (int y = 0; y < Balance.GridH; y++)
                    if (Temp[x, y] < Balance.AmbientTemp) Temp[x, y] = Balance.AmbientTemp;

            foreach (Building b in buildings)
                if (!b.Removed)
                    b.TileTemp = Temp[b.X, b.Y];
        }

        private void Stamp(int cx, int cy, float amp, float radius, float sign)
        {
            int r = Mathf.CeilToInt(radius);
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= Balance.GridW || y >= Balance.GridH) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    Temp[x, y] += sign * amp * (1f - d / radius);
                }
            }
        }

        public float At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Balance.GridW || y >= Balance.GridH) return Balance.AmbientTemp;
            return Temp[x, y];
        }
    }
}
