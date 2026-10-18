using System;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

/// <summary>V1：Fixed64 除乘法以外的部分——构造、常量、加减、取负、比较、相等、ToString。</summary>
public class Fixed64Tests
{
    const long One = 1L << 32;
    const long Half = 1L << 31;
    const long Max = long.MaxValue;
    const long Min = long.MinValue;

    static Fixed64 R(long raw) => Fixed64.FromRaw(raw);

    [Test]
    public void ConstantsHaveExpectedRawValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Zero.RawValue, Is.EqualTo(0L));
            Assert.That(Fixed64.One.RawValue, Is.EqualTo(One));
            Assert.That(Fixed64.Half.RawValue, Is.EqualTo(Half));
            Assert.That(Fixed64.Epsilon.RawValue, Is.EqualTo(1L));
            Assert.That(Fixed64.MinValue.RawValue, Is.EqualTo(Min));
            Assert.That(Fixed64.MaxValue.RawValue, Is.EqualTo(Max));
            Assert.That(default(Fixed64), Is.EqualTo(Fixed64.Zero));
        });
    }

    [TestCase(0, 0L)]
    [TestCase(1, One)]
    [TestCase(-1, -One)]
    [TestCase(int.MaxValue, (long)int.MaxValue << 32)]
    [TestCase(int.MinValue, Min)]
    public void FromIntShiftsIntoIntegerPart(int value, long expectedRaw)
    {
        Assert.That(Fixed64.FromInt(value).RawValue, Is.EqualTo(expectedRaw));
    }

    [TestCase(0L)]
    [TestCase(1L)]
    [TestCase(-1L)]
    [TestCase(Max)]
    [TestCase(Min)]
    public void FromRawKeepsRawValue(long raw)
    {
        Assert.That(R(raw).RawValue, Is.EqualTo(raw));
    }

    // 期望值是手算的；随机用例再和 BigInteger 参考实现比对。
    [TestCase(One, One, 2 * One)]
    [TestCase(Half, -One, -Half)]
    [TestCase(Max, 1L, Max)]                // 刚好溢出 → 饱和
    [TestCase(Max - 1, 1L, Max)]            // 刚好不溢出
    [TestCase(Min, -1L, Min)]
    [TestCase(Min + 1, -1L, Min)]
    [TestCase(Max, Max, Max)]
    [TestCase(Min, Min, Min)]
    [TestCase(Max, Min, -1L)]
    public void AddSaturates(long a, long b, long expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Add(R(a), R(b)).RawValue, Is.EqualTo(expected));
            Assert.That((R(a) + R(b)).RawValue, Is.EqualTo(expected));
            Assert.That((R(b) + R(a)).RawValue, Is.EqualTo(expected));
        });
    }

    [TestCase(One, Half, Half)]
    [TestCase(Half, One, -Half)]
    [TestCase(Min, 1L, Min)]                // 刚好溢出 → 饱和
    [TestCase(Min + 1, 1L, Min)]            // 刚好不溢出
    [TestCase(Max, -1L, Max)]
    [TestCase(0L, Min, Max)]                // 0 - MinValue = 2^63
    [TestCase(-1L, Min, Max)]               // 2^63 - 1，刚好不溢出
    [TestCase(-1L, Max, Min)]               // -2^63，刚好不溢出
    [TestCase(-2L, Max, Min)]               // 刚好溢出
    [TestCase(Max, Min, Max)]
    [TestCase(Min, Max, Min)]
    public void SubSaturates(long a, long b, long expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Sub(R(a), R(b)).RawValue, Is.EqualTo(expected));
            Assert.That((R(a) - R(b)).RawValue, Is.EqualTo(expected));
        });
    }

    [TestCase(0L, 0L)]
    [TestCase(1L, -1L)]
    [TestCase(-One, One)]
    [TestCase(Max, Min + 1)]
    [TestCase(Min, Max)]                    // 饱和
    public void NegateSaturates(long a, long expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Negate(R(a)).RawValue, Is.EqualTo(expected));
            Assert.That((-R(a)).RawValue, Is.EqualTo(expected));
        });
    }

    [Test]
    public void AddSubNegateMatchReferenceOnRandomCases()
    {
        var generator = new Fixed64CaseGenerator();
        for (int i = 0; i < Fixed64CaseGenerator.RandomCaseCount; i++)
        {
            var (a, b) = generator.NextPair();
            long sum = (R(a) + R(b)).RawValue, diff = (R(a) - R(b)).RawValue, neg = (-R(a)).RawValue;
            if (sum != Fixed64Reference.Add(a, b) || diff != Fixed64Reference.Sub(a, b) || neg != Fixed64Reference.Negate(a))
                Assert.Fail($"#{i}: a=0x{a:X16} b=0x{b:X16}：a+b=0x{sum:X16} 期望 0x{Fixed64Reference.Add(a, b):X16}；"
                    + $"a-b=0x{diff:X16} 期望 0x{Fixed64Reference.Sub(a, b):X16}；-a=0x{neg:X16} 期望 0x{Fixed64Reference.Negate(a):X16}");
        }
    }

    [Test]
    public void ComparisonAndEqualityFollowRawValue()
    {
        var generator = new Fixed64CaseGenerator();
        for (int i = 0; i < Fixed64CaseGenerator.RandomCaseCount; i++)
        {
            var (a, b) = generator.NextPair();
            // 每 8 组拿一组自己和自己比，保证相等的分支也被覆盖。
            if (i % 8 == 0)
                b = a;
            Fixed64 x = R(a), y = R(b);
            bool ok = (x < y) == (a < b) && (x > y) == (a > b) && (x <= y) == (a <= b) && (x >= y) == (a >= b)
                && (x == y) == (a == b) && (x != y) == (a != b)
                && x.Equals(y) == (a == b) && x.Equals((object)y) == (a == b)
                && Math.Sign(x.CompareTo(y)) == a.CompareTo(b)
                && (a != b || x.GetHashCode() == y.GetHashCode());
            if (!ok)
                Assert.Fail($"#{i}: a=0x{a:X16} b=0x{b:X16} 的比较或相等结果和原始值不一致");
        }
    }

    [Test]
    public void OrderingAtBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.MinValue < -Fixed64.Epsilon, Is.True);
            Assert.That(-Fixed64.Epsilon < Fixed64.Zero, Is.True);
            Assert.That(Fixed64.Zero < Fixed64.Epsilon, Is.True);
            Assert.That(Fixed64.Epsilon < Fixed64.MaxValue, Is.True);
            Assert.That(Fixed64.MinValue.CompareTo(Fixed64.MaxValue), Is.Negative);
            Assert.That(Fixed64.Zero.Equals(null), Is.False);
            Assert.That(Fixed64.Zero.Equals((object)0L), Is.False);
        });
    }

    [TestCase(0L, "0")]
    [TestCase(One, "1")]
    [TestCase(-3 * One, "-3")]
    [TestCase(One + Half, "1.5")]
    [TestCase(-Half, "-0.5")]
    [TestCase(1L, "0.0000000002")]          // 2^-32 ≈ 2.328e-10，截断到 10 位
    [TestCase(-1L, "-0.0000000002")]
    [TestCase(0xFFFFFFFFL, "0.9999999997")] // 1 - 2^-32
    [TestCase(Max, "2147483647.9999999997")]
    [TestCase(Min, "-2147483648")]
    public void ToStringIsExactDecimalTruncatedToTenPlaces(long raw, string expected)
    {
        Assert.That(R(raw).ToString(), Is.EqualTo(expected));
    }
}
