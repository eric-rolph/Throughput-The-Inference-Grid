using UnityEngine;

namespace Throughput.Game
{
    /// Procedurally generated sprites — no imported art assets.
    public static class SpriteFactory
    {
        public static Sprite Circle { get; private set; }
        public static Sprite Halo { get; private set; }
        public static Sprite Pixel { get; private set; }
        public static Sprite Ring { get; private set; }
        public static Sprite RackBody { get; private set; }
        public static Sprite[] LedGrid { get; private set; }   // 0 busy, 1 sparse, 2 dark
        public static Sprite FanBlade { get; private set; }
        public static Sprite PduBody { get; private set; }
        public static Sprite CracBody { get; private set; }
        public static Sprite Chevron { get; private set; }     // uplink/feed marker
        public static Sprite Smoke { get; private set; }
        public static Sprite ArcFlash { get; private set; }

        private static bool _built;

        public static void Build()
        {
            if (_built) return;
            _built = true;
            Circle = Make(48, (x, y, n) => Dist(x, y, n) <= 0.85f ? 1f : 0f);
            Halo = Make(48, (x, y, n) => { float d = Dist(x, y, n); return Mathf.Pow(Mathf.Clamp01(1f - d), 2f) * 0.9f; });
            Pixel = Make(4, (x, y, n) => 1f);
            Ring = Make(96, (x, y, n) => { float d = Dist(x, y, n); return (d > 0.94f && d <= 1f) ? 1f : 0f; });
            RackBody = Make(64, (x, y, n) => Body(x, y, n, border: 4));
            LedGrid = new[] { MakeLed(0), MakeLed(1), MakeLed(2) };
            FanBlade = MakeFan();
            PduBody = Make(64, (x, y, n) => Body(x, y, n, border: 6));
            CracBody = Make(64, (x, y, n) =>
            {
                float body = Body(x, y, n, border: 4);
                // horizontal vent slits
                if (body > 0.9f && (y / 6) % 2 == 0 && x > 12 && x < 52) return 0.55f;
                return body;
            });
            Chevron = Make(48, (x, y, n) =>
            {
                float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
                return Mathf.Abs(fy - 0.5f) < 0.42f - Mathf.Abs(fx - 0.62f) ? 1f : 0f;
            });
            Smoke = Make(32, (x, y, n) => { float d = Dist(x, y, n); return Mathf.Clamp01(1f - d) * 0.5f; });
            ArcFlash = Make(96, (x, y, n) =>
            {
                float d = Dist(x, y, n);
                float spikes = Mathf.Abs(Mathf.Sin(Mathf.Atan2(y - n / 2f, x - n / 2f) * 6f));
                return d < 0.25f + spikes * 0.7f ? Mathf.Clamp01(1.2f - d) : 0f;
            });
        }

        private static float Dist(int x, int y, int n)
        {
            float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
            return Mathf.Sqrt(dx * dx + dy * dy) * 2f;
        }

        private static float Body(int x, int y, int n, int border)
        {
            bool edge = x < border || y < border || x >= n - border || y >= n - border;
            return edge ? 1f : 0.82f;
        }

        private static Sprite MakeLed(int mode)
        {
            // 8x5 LED matrix inside a 64px sprite; mode 0 = mostly lit, 1 = sparse, 2 = off
            return Make(64, (x, y, n) =>
            {
                int cx = (x - 8) / 6, cy = (y - 20) / 6;
                if (x < 8 || y < 20 || cx > 7 || cy > 4) return 0f;
                bool inLed = (x - 8) % 6 < 4 && (y - 20) % 6 < 4;
                if (!inLed) return 0f;
                int hash = (cx * 31 + cy * 17 + mode * 7) % 10;
                if (mode == 0) return hash < 8 ? 1f : 0.15f;
                if (mode == 1) return hash < 3 ? 1f : 0.08f;
                return 0.05f;
            });
        }

        private static Sprite MakeFan()
        {
            return Make(48, (x, y, n) =>
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                if (d > 0.95f) return 0f;
                if (d < 0.18f) return 1f;
                float ang = Mathf.Atan2(dy, dx);
                float blade = Mathf.Abs(Mathf.Sin(ang * 2f + d * 3f));
                return blade > 0.55f && d < 0.9f ? 0.9f : 0f;
            });
        }

        private static Sprite Make(int n, System.Func<int, int, int, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color(1f, 1f, 1f, alpha(x, y, n));
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
    }
}
