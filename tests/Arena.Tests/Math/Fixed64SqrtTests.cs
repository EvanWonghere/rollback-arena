using System;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

public class Fixed64SqrtTests
{
    const long One = 1L << 32;

    [TestCase(0L, 0L)]
    [TestCase(1L, 65536L)]
    [TestCase(One, One)]
    [TestCase(4 * One, 2 * One)]
    [TestCase(9 * One, 3 * One)]
    [TestCase(One / 4, One / 2)]
    [TestCase(2 * One, 6074001000L)]
    public void BoundaryCases(long raw, long expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Sqrt(Fixed64.FromRaw(raw)).RawValue, Is.EqualTo(expected));
            Assert.That(ExtendedMathReference.Sqrt(raw), Is.EqualTo(expected), "independent oracle");
        });
    }

    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    public void NegativeInputThrows(long raw) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed64.Sqrt(Fixed64.FromRaw(raw)));

    [Test]
    public void MillionRootsMatchIndependentReference()
    {
        var generator = new Fixed64CaseGenerator();
        int tiny = 0, nearSquares = 0, large = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            long raw;
            switch (i % 4)
            {
                case 0: raw = (long)(generator.NextUInt64() & long.MaxValue); large++; break;
                case 1: raw = (long)(generator.NextUInt64() % 65536); tiny++; break;
                case 2:
                    long n = (long)(generator.NextUInt64() % 46341);
                    raw = Math.Max(0, (n * n << 32) + (i % 3 - 1)); nearSquares++; break;
                default: raw = long.MaxValue - (long)(generator.NextUInt64() % 1024); large++; break;
            }
            long expected = ExtendedMathReference.Sqrt(raw);
            long actual = Fixed64.Sqrt(Fixed64.FromRaw(raw)).RawValue;
            if (expected != actual) Assert.Fail($"#{i}: sqrt raw {raw}: expected {expected}, got {actual}");
        }
        Assert.Multiple(() =>
        {
            Assert.That(tiny, Is.EqualTo(250_000));
            Assert.That(nearSquares, Is.EqualTo(250_000));
            Assert.That(large, Is.EqualTo(500_000));
        });
        TestContext.Out.WriteLine($"Sqrt 1,000,000: tiny={tiny}, square-neighbors={nearSquares}, large={large}");
    }

    [Test]
    public void ScalarHelpersRespectSaturationAndOrdering()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Abs(Fixed64.MinValue), Is.EqualTo(Fixed64.MaxValue));
            Assert.That(Fixed64.Abs(-Fixed64.Epsilon), Is.EqualTo(Fixed64.Epsilon));
            Assert.That(Fixed64.Abs(Fixed64.Zero), Is.EqualTo(Fixed64.Zero));
            Assert.That(Fixed64.Min(Fixed64.MinValue, Fixed64.MaxValue), Is.EqualTo(Fixed64.MinValue));
            Assert.That(Fixed64.Max(Fixed64.MinValue, Fixed64.MaxValue), Is.EqualTo(Fixed64.MaxValue));
        });
    }
}
