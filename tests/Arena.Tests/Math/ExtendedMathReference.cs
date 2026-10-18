using System;
using System.Numerics;

namespace Arena.Tests.FixedMath;

/// <summary>测试侧宽整数 oracle；不复用生产的逐位除法、逐两位开方或向量缩放。</summary>
public static class ExtendedMathReference
{
    public static long Div(long a, long b)
    {
        if (b == 0) throw new DivideByZeroException();
        BigInteger numerator = (BigInteger)a << 32;
        BigInteger quotient = BigInteger.DivRem(BigInteger.Abs(numerator), BigInteger.Abs(b), out var remainder);
        if (2 * remainder >= BigInteger.Abs(b)) quotient++;
        return Fixed64Reference.Saturate((a < 0) ^ (b < 0) ? -quotient : quotient);
    }

    public static BigInteger FloorRoot(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n.IsZero) return BigInteger.Zero;
        BigInteger root = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            BigInteger next = (root + n / root) / 2;
            if (next >= root) return root;
            root = next;
        }
    }

    public static BigInteger NearestRoot(BigInteger n)
    {
        BigInteger root = FloorRoot(n);
        return 4 * n >= (2 * root + 1) * (2 * root + 1) ? root + 1 : root;
    }

    public static long Sqrt(long raw)
    {
        if (raw < 0) throw new ArgumentOutOfRangeException(nameof(raw));
        return (long)NearestRoot((BigInteger)raw << 32);
    }

    public static (long Length, long X, long Y, long Z) Vector(long x, long y, long z)
    {
        BigInteger sum = (BigInteger)x * x + (BigInteger)y * y + (BigInteger)z * z;
        if (sum.IsZero) return (0, 0, 0, 0);
        long length = Fixed64Reference.Saturate(NearestRoot(sum));
        // 根保留额外 64 位二进制小数；与生产缩放到 Fixed64 的方法独立。
        BigInteger root = FloorRoot(sum << 128);
        long Component(long raw)
        {
            BigInteger numerator = BigInteger.Abs((BigInteger)raw) << 96;
            BigInteger q = BigInteger.DivRem(numerator, root, out var remainder);
            if (2 * remainder >= root) q++;
            return (long)(raw < 0 ? -q : q);
        }
        return (length, Component(x), Component(y), Component(z));
    }
}
