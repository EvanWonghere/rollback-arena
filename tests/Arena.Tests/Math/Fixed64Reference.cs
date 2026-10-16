using System.Numerics;

namespace Arena.Tests.FixedMath;

/// <summary>
/// V1 的参考实现：用 BigInteger 算出精确结果，再按方案 S1 定下的规则处理，所有输入输出都是原始值（RawValue）。
/// 规则：溢出时饱和（精确结果夹到 long 的范围）；乘法四舍五入，恰好一半时远离零。
/// 这里故意写得最直白，正确性靠 <see cref="Fixed64ReferenceTests"/> 里手算的期望值来保证。
/// </summary>
public static class Fixed64Reference
{
    static readonly BigInteger Min = long.MinValue;
    static readonly BigInteger Max = long.MaxValue;
    static readonly BigInteger HalfUlp = BigInteger.One << 31;

    public static long Saturate(BigInteger exact) =>
        exact < Min ? long.MinValue : exact > Max ? long.MaxValue : (long)exact;

    public static long Add(long a, long b) => Saturate((BigInteger)a + b);

    public static long Sub(long a, long b) => Saturate((BigInteger)a - b);

    public static long Negate(long a) => Saturate(-(BigInteger)a);

    /// <summary>精确值是 a·b / 2^32；对绝对值加半个 ulp 再右移，就是「一半时远离零」。</summary>
    public static long Mul(long a, long b)
    {
        BigInteger product = (BigInteger)a * b;
        BigInteger rounded = (BigInteger.Abs(product) + HalfUlp) >> 32;
        return Saturate(product.Sign < 0 ? -rounded : rounded);
    }

    /// <summary>乘积的绝对值恰好落在两个可表示值正中间（低 32 位等于 2^31），这时舍入规则才起作用。</summary>
    public static bool IsMulTie(long a, long b) =>
        (BigInteger.Abs((BigInteger)a * b) & uint.MaxValue) == HalfUlp;
}
