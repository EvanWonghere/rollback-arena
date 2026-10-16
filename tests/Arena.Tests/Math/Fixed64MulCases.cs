using System.Collections.Generic;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

/// <summary>
/// 乘法的边界用例。期望值是手算的（推导写在注释里），不是调参考实现得来的，
/// 所以 <see cref="Fixed64ReferenceTests"/> 能用它们反过来检查参考实现。
/// 每条用例会正反两个顺序各出现一次。
/// </summary>
public static class Fixed64MulCases
{
    const long One = 1L << 32;
    const long Half = 1L << 31;
    const long Max = long.MaxValue;
    const long Min = long.MinValue;
    const long FracOnes = 0xFFFFFFFFL; // 小数位全为 1，整数部分为 0，即 1 - ε

    static long I(int n) => (long)n * One;

    static readonly (string Name, long A, long B, long Expected)[] Cases =
    {
        ("0×0", 0, 0, 0),
        ("0×MaxValue", 0, Max, 0),
        ("0×MinValue", 0, Min, 0),
        ("1×1", One, One, One),
        ("-1×1", -One, One, -One),
        ("-1×-1", -One, -One, One),
        ("1×MaxValue", One, Max, Max),
        ("1×MinValue", One, Min, Min),
        ("-1×MaxValue", -One, Max, Min + 1),
        ("-1×MinValue 饱和", -One, Min, Max), // 精确值 2^63
        ("2×3/2", I(2), One + Half, I(3)),

        // ε 相关：精确值 = a·b / 2^32 个 ulp。
        ("ε×ε 舍到 0", 1, 1, 0),           // 2^-32 ulp
        ("-ε×ε 舍到 0", -1, 1, 0),
        ("-ε×-ε 舍到 0", -1, -1, 0),
        ("ε×1", 1, One, 1),
        ("-ε×1", -1, One, -1),
        ("ε×½ 恰好一半远离零", 1, Half, 1),  // 0.5 ulp
        ("-ε×½ 恰好一半远离零", -1, Half, -1),
        ("-ε×-½ 恰好一半远离零", -1, -Half, 1),
        ("3ε×½ 恰好一半远离零", 3, Half, 2), // 1.5 ulp
        ("-3ε×½ 恰好一半远离零", -3, Half, -2),
        ("ε×(½-ε) 不到一半", 1, Half - 1, 0),
        ("-ε×(½-ε) 不到一半", -1, Half - 1, 0),
        ("ε×(½+ε) 过一半", 1, Half + 1, 1),
        ("ε×MaxValue", 1, Max, Half),        // (2^63-1)/2^32 = 2^31 - 2^-32
        ("ε×MinValue", 1, Min, -Half),       // -2^31，精确

        // 两个极值。
        ("MaxValue×MaxValue 饱和", Max, Max, Max),
        ("MinValue×MinValue 饱和", Min, Min, Max),
        ("MaxValue×MinValue 饱和", Max, Min, Min),
        ("MaxValue×½ 恰好一半远离零", Max, Half, 1L << 62),   // (2^63-1)/2 = 2^62 - 0.5
        ("MinValue×½", Min, Half, -(1L << 62)),
        ("MinValue×-½", Min, -Half, 1L << 62),
        ("MaxValue×(1-ε)", Max, FracOnes, Max - Half),      // 2^63 - 2^31 - 1 + 2^-32
        ("MinValue×(1-ε)", Min, FracOnes, Min + Half),      // -2^63 + 2^31，精确

        // 乘积刚好溢出的两侧：2 × x 的精确值就是 2x，没有舍入。
        ("2×(2^62-1)ε 刚好不溢出", I(2), (1L << 62) - 1, Max - 1),
        ("2×2^62ε 刚好溢出", I(2), 1L << 62, Max),
        ("2×-2^62ε 刚好等于 MinValue", I(2), -(1L << 62), Min),
        ("2×(-2^62-1)ε 刚好溢出", I(2), -(1L << 62) - 1, Min),
        ("-2×(-2^62+1)ε 负×负刚好不溢出", I(-2), -(1L << 62) + 1, Max - 1),
        ("-2×-2^62ε 负×负刚好溢出", I(-2), -(1L << 62), Max),
        // 整数版本：46340² = 2147395600 ≤ 2^31-1 < 46341² = 2147488281。
        ("46340×46340 不溢出", I(46340), I(46340), I(2147395600)),
        ("46341×46341 溢出", I(46341), I(46341), Max),
        ("-46340×-46340 负×负不溢出", I(-46340), I(-46340), I(2147395600)),
        ("-46341×-46341 负×负溢出", I(-46341), I(-46341), Max),
        ("-46340×46341 不溢出", I(-46340), I(46341), I(-2147441940)),
        ("-46341×46341 溢出", I(-46341), I(46341), Min),

        // 负×负。
        ("-3×-4", I(-3), I(-4), I(12)),
        ("-½×-½", -Half, -Half, 1L << 30),
        ("-3/2×-3/2", -(One + Half), -(One + Half), I(2) + (1L << 30)),

        // 小数位全为 1。
        ("(1-ε)×(1-ε)", FracOnes, FracOnes, FracOnes - 1),     // (2^32-1)²/2^32 = 2^32 - 2 + 2^-32
        ("-(1-ε)×(1-ε)", -FracOnes, FracOnes, -(FracOnes - 1)),
        ("-(1-ε)×-(1-ε)", -FracOnes, -FracOnes, FracOnes - 1),
        ("(1-ε)×1", FracOnes, One, FracOnes),
        ("(1-ε)×½ 恰好一半远离零", FracOnes, Half, Half),         // (2^32-1)/2 = 2^31 - 0.5
        ("-(1-ε)×½ 恰好一半远离零", -FracOnes, Half, -Half),
        ("(1-ε)×ε", FracOnes, 1, 1),                            // 1 - 2^-32 ulp
        ("(2-ε)×(2-ε)", One + FracOnes, One + FracOnes, (1L << 34) - 4), // (2^33-1)²/2^32 = 2^34 - 4 + 2^-32
    };

    /// <summary>参数：a 的原始值、b 的原始值、手算的期望原始值。</summary>
    public static IEnumerable<TestCaseData> All()
    {
        foreach (var (name, a, b, expected) in Cases)
        {
            yield return new TestCaseData(a, b, expected).SetArgDisplayNames(name);
            if (a != b)
                yield return new TestCaseData(b, a, expected).SetArgDisplayNames(name + " 交换");
        }
    }
}
