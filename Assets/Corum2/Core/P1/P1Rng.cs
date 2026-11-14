using System;

namespace Corum2.Core.P1
{
    /// <summary>
    /// Deterministic random stream whose whole state is one 64-bit value, so it can be saved and resumed without
    /// re-rolling (GRO-48, MER-29). xorshift64* (state never 0).
    /// </summary>
    public sealed class P1Rng
    {
        public ulong State;

        public P1Rng(ulong seed) => State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        public ulong NextU64()
        {
            var x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextU64() % (ulong)(maxExclusive - minInclusive));
        }

        /// <summary>Index chosen by weight; -1 if all weights are 0.</summary>
        public int Weighted(System.Collections.Generic.IReadOnlyList<double> weights)
        {
            double sum = 0;
            foreach (var w in weights) if (w > 0) sum += w;
            if (sum <= 0) return -1;
            var r = NextDouble() * sum;
            for (var i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) continue;
                r -= weights[i];
                if (r < 0) return i;
            }
            for (var i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0) return i;
            return -1;
        }

        public static ulong Mix(ulong a, ulong b)
        {
            var z = a + 0x9E3779B97F4A7C15UL * (b + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
