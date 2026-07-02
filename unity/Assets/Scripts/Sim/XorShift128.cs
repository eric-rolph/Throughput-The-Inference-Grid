namespace Throughput.Sim
{
    /// Deterministic RNG — the single source of randomness for the whole sim.
    public sealed class XorShift128
    {
        private uint x, y, z, w;

        public XorShift128(ulong seed)
        {
            x = (uint)(seed & 0xFFFFFFFF);
            y = (uint)(seed >> 32) | 1u;
            z = 0x9E3779B9u ^ x;
            w = 0x85EBCA6Bu ^ y;
            for (int i = 0; i < 8; i++) NextUInt();
        }

        public uint NextUInt()
        {
            uint t = x ^ (x << 11);
            x = y; y = z; z = w;
            w = w ^ (w >> 19) ^ (t ^ (t >> 8));
            return w;
        }

        /// [0, 1)
        public float NextFloat() => (NextUInt() & 0xFFFFFF) / 16777216f;

        /// [min, max)
        public int Range(int min, int max) => min + (int)(NextUInt() % (uint)(max - min));

        public float Range(float min, float max) => min + NextFloat() * (max - min);
    }
}
