using System;

namespace Arena.Sim
{
    /// <summary>SplitMix64。复制整个结构体可保存随机序列位置；默认值与 seed=0 等价。</summary>
    public struct DeterministicRandom
    {
        // Sebastiano Vigna, public domain / CC0: https://prng.di.unimi.it/splitmix64.c
        ulong state;

        public DeterministicRandom(ulong seed) { state = seed; }
        public ulong State => state;

        public ulong NextUInt64()
        {
            unchecked
            {
                ulong z = state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>均匀整数 [0, exclusiveMax)。非法上界不消耗随机状态。</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            ulong bound = (ulong)exclusiveMax;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong sample;
            do { sample = NextUInt64(); } while (sample < threshold);
            return (int)(sample % bound);
        }
    }
}
