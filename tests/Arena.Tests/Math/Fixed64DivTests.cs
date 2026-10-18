using System;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

public class Fixed64DivTests
{
    const long One = 1L << 32;

    [TestCase(0L, One, 0L)]
    [TestCase(One, One, One)]
    [TestCase(-One, One, -One)]
    [TestCase(-One, -One, One)]
    [TestCase(3 * One, 2 * One, One + (One >> 1))]
    [TestCase(1L, 2 * One, 1L)]
    [TestCase(-1L, 2 * One, -1L)]
    [TestCase(3L, 2 * One, 2L)]
    [TestCase(1L, 2 * One + 1, 0L)]
    [TestCase(1L, 2 * One - 1, 1L)]
    [TestCase(long.MinValue, One, long.MinValue)]
    [TestCase(long.MinValue, -One, long.MaxValue)]
    [TestCase(long.MaxValue, 1L, long.MaxValue)]
    [TestCase(long.MinValue, 1L, long.MinValue)]
    [TestCase(long.MinValue, long.MinValue, One)]
    [TestCase(long.MaxValue, long.MinValue, -One)]
    public void BoundaryCases(long a, long b, long expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fixed64.Div(Fixed64.FromRaw(a), Fixed64.FromRaw(b)).RawValue, Is.EqualTo(expected));
            Assert.That((Fixed64.FromRaw(a) / Fixed64.FromRaw(b)).RawValue, Is.EqualTo(expected));
            Assert.That(ExtendedMathReference.Div(a, b), Is.EqualTo(expected), "reference vs hand-computed");
        });
    }

    [TestCase(0L)]
    [TestCase(One)]
    [TestCase(long.MinValue)]
    public void DivisionByZeroThrows(long a) =>
        Assert.Throws<DivideByZeroException>(() => _ = Fixed64.FromRaw(a) / Fixed64.Zero);

    [Test]
    public void MillionDivisionsMatchIndependentReferenceWithOutcomeCoverage()
    {
        var generator = new Fixed64CaseGenerator();
        int high = 0, low = 0, positive = 0, negative = 0, exact = 0, ties = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            var (a, b) = generator.NextPair();
            // 固定比例构造可表示商上的半途情况，不靠偶然抽到。
            if (i % 8 == 0) { a = (long)(generator.NextUInt64() % 1_000_000) * 2 + 1; b = 2 * One; }
            if (b == 0) b = 1;
            long expected = ExtendedMathReference.Div(a, b);
            long actual = (Fixed64.FromRaw(a) / Fixed64.FromRaw(b)).RawValue;
            if (expected != actual) Assert.Fail($"#{i}: raw {a}/{b}: expected {expected}, got {actual}");
            if (expected == long.MaxValue) high++;
            else if (expected == long.MinValue) low++;
            else
            {
                if (expected > 0) positive++;
                if (expected < 0) negative++;
                var remainder = System.Numerics.BigInteger.Abs(((System.Numerics.BigInteger)a << 32) % b);
                if (remainder.IsZero) exact++;
                if (2 * remainder == System.Numerics.BigInteger.Abs(b)) ties++;
            }
        }
        Assert.Multiple(() =>
        {
            Assert.That(high, Is.GreaterThan(10_000));
            Assert.That(low, Is.GreaterThan(10_000));
            Assert.That(positive, Is.GreaterThan(10_000));
            Assert.That(negative, Is.GreaterThan(10_000));
            Assert.That(exact, Is.GreaterThan(10_000));
            Assert.That(ties, Is.GreaterThan(10_000));
        });
        TestContext.Out.WriteLine($"Div 1,000,000: high={high}, low={low}, positive={positive}, negative={negative}, exact={exact}, ties={ties}");
    }
}
