namespace Arena.Tests.FixedMath;

/// <summary>
/// 固定种子的随机用例生成器，生成 Fixed64 的原始值。
/// 用自己写的 SplitMix64，而不是 System.Random：序列不随 .NET 版本变化，失败时能按种子和序号复现。
/// </summary>
/// <remarks>
/// 全范围均匀抽样时，几乎所有乘积都会溢出，所以按几种策略混合抽取操作数，
/// 让结果覆盖不溢出、正负两侧饱和、恰好一半的舍入。覆盖率由 <see cref="Fixed64ReferenceTests"/> 检查。
/// </remarks>
public sealed class Fixed64CaseGenerator
{
    public const ulong DefaultSeed = 0x5EED_F1C5_0001UL;
    public const int RandomCaseCount = 1_000_000;

    const long One = 1L << 32;
    const long Half = 1L << 31;

    // 边界附近的值；抽到之后再加一个 [-4, 4] 的小偏移。
    static readonly long[] Interesting =
    {
        0, One, -One, Half, -Half, 1, -1, long.MaxValue, long.MinValue, 0xFFFFFFFFL,
        46340L * One, 46341L * One, 1L << 62, -(1L << 62), 1L << 47,
        0xB504F333F9DEL, // ≈ √2·2^47：自乘的结果在 MaxValue 附近
    };

    ulong _state;

    public Fixed64CaseGenerator(ulong seed = DefaultSeed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // 取模有极小的偏差，对用例分布无所谓。
    int NextInt(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);

    long RandomSign(long magnitude) => (NextUInt64() & 1) == 0 ? magnitude : -magnitude;

    public long NextOperand()
    {
        switch (NextInt(5))
        {
            case 0: // 全范围均匀：大多数乘积会溢出。
                return (long)NextUInt64();
            case 1: // 量级对数均匀（0–63 位有效位）：乘积从很小一路跨到溢出。
            {
                int bits = NextInt(64);
                ulong mask = bits == 0 ? 0 : ulong.MaxValue >> (64 - bits);
                return RandomSign((long)(NextUInt64() & mask));
            }
            case 2: // 边界值附近，越界时按 long 回绕到另一头，正好也是边界。
                return unchecked(Interesting[NextInt(Interesting.Length)] + NextInt(9) - 4);
            case 3: // n + 0.5：和奇数原始值相乘时，乘积恰好落在两个可表示值正中间。
                return RandomSign((long)NextInt(1 << 20) * One + Half);
            default: // 整数，覆盖 int 的全范围。
                return (long)(int)NextUInt64() * One;
        }
    }

    public (long A, long B) NextPair() => (NextOperand(), NextOperand());
}
