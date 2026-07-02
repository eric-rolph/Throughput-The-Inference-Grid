using UnityEngine;

namespace Throughput.Game
{
    /// Procedurally generated sprites — no imported art assets.
    public static class SpriteFactory
    {
        public static Sprite Circle { get; private set; }
        public static Sprite Triangle { get; private set; }
        public static Sprite Square { get; private set; }
        public static Sprite Halo { get; private set; }
        public static Sprite Pixel { get; private set; }
        public static Sprite Ring { get; private set; }

        private static bool _built;

        public static void Build()
        {
            if (_built) return;
            _built = true;
            Circle = MakeSprite(64, (x, y) => InsideCircle(x, y, 64) ? 1f : 0f);
            Triangle = MakeSprite(64, (x, y) => InsideTriangle(x, y, 64) ? 1f : 0f);
            Square = MakeSprite(64, (x, y) => InsideSquare(x, y, 64) ? 1f : 0f);
            Halo = MakeSprite(64, (x, y) => HaloAlpha(x, y, 64));
            Pixel = MakeSprite(4, (x, y) => 1f);
            Ring = MakeSprite(64, (x, y) => RingAlpha(x, y, 64));
        }

        private static bool InsideCircle(int x, int y, int n)
        {
            float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
            return dx * dx + dy * dy <= (n * 0.42f) * (n * 0.42f);
        }

        private static bool InsideTriangle(int x, int y, int n)
        {
            float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
            // upward triangle: apex top-center
            float half = 0.42f * (1f - (fy - 0.08f) / 0.84f);
            return fy >= 0.08f && fy <= 0.92f && Mathf.Abs(fx - 0.5f) <= half;
        }

        private static bool InsideSquare(int x, int y, int n)
        {
            float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
            return fx > 0.14f && fx < 0.86f && fy > 0.14f && fy < 0.86f;
        }

        private static float HaloAlpha(int x, int y, int n)
        {
            float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
            return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 0.9f;
        }

        private static float RingAlpha(int x, int y, int n)
        {
            float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
            return (d > 0.72f && d <= 0.95f) ? 1f : 0f;
        }

        private static Sprite MakeSprite(int n, System.Func<int, int, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color(1f, 1f, 1f, alpha(x, y));
            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
    }
}
