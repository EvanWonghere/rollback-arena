using System;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

public class FixMathTests
{
    const double Scale = 4294967296.0;

    [Test]
    public void ConstantsAndCardinalAnglesMatchIndependentValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FixMath.Pi.RawValue, Is.EqualTo(13493037705L));
            Assert.That(FixMath.TwoPi.RawValue, Is.EqualTo(26986075409L));
            Assert.That(FixMath.HalfPi.RawValue, Is.EqualTo(6746518852L));
            Assert.That(FixMath.Sin(Fixed64.Zero), Is.EqualTo(Fixed64.Zero));
            Assert.That(FixMath.Cos(Fixed64.Zero), Is.EqualTo(Fixed64.One));
            Assert.That(Math.Abs(FixMath.Sin(FixMath.HalfPi).RawValue - Fixed64.One.RawValue), Is.LessThanOrEqualTo(2));
            Assert.That(Math.Abs(FixMath.Cos(FixMath.Pi).RawValue + Fixed64.One.RawValue), Is.LessThanOrEqualTo(2));
            Assert.That(Math.Abs(FixMath.Sin(FixMath.Pi).RawValue), Is.LessThanOrEqualTo(2));
        });
    }

    [Test]
    public void MillionAnglesMeetIndependentAbsoluteErrorBound()
    {
        var generator = new Fixed64CaseGenerator();
        double worstSin = 0, worstCos = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            // 包含全周期的密集扫描，再覆盖声明的整个角度范围。
            long raw = i < 100_000
                ? i * FixMath.TwoPi.RawValue / 100_000
                : (long)(generator.NextUInt64() % (2048UL << 32)) - (1024L << 32);
            double angle = raw / Scale;
            double sinError = Math.Abs(FixMath.Sin(Fixed64.FromRaw(raw)).RawValue / Scale - Math.Sin(angle));
            double cosError = Math.Abs(FixMath.Cos(Fixed64.FromRaw(raw)).RawValue / Scale - Math.Cos(angle));
            worstSin = Math.Max(worstSin, sinError);
            worstCos = Math.Max(worstCos, cosError);
            if (sinError > 1e-6 || cosError > 1e-6)
                Assert.Fail($"#{i}: angle raw={raw}, sin error={sinError:R}, cos error={cosError:R}");
        }
        TestContext.Out.WriteLine($"Sin/Cos 1,000,000: max absolute error sin={worstSin:R}, cos={worstCos:R}");
    }

    [Test]
    public void PeriodSeamsAndDeclaredRangeEndpointsMeetErrorBound()
    {
        foreach (long center in new[] { -1024L << 32, 1024L << 32, 0, FixMath.TwoPi.RawValue, -FixMath.TwoPi.RawValue })
        {
            for (int offset = -4; offset <= 4; offset++)
            {
                long raw = center + offset;
                double angle = raw / Scale;
                Assert.That(FixMath.Sin(Fixed64.FromRaw(raw)).RawValue / Scale, Is.EqualTo(Math.Sin(angle)).Within(1e-6));
                Assert.That(FixMath.Cos(Fixed64.FromRaw(raw)).RawValue / Scale, Is.EqualTo(Math.Cos(angle)).Within(1e-6));
            }
        }
    }

    [Test]
    public void AllRawMagnitudesRemainBoundedAndRepeatable()
    {
        var generator = new Fixed64CaseGenerator();
        long[] extremes = { long.MinValue, long.MinValue + 1, long.MaxValue, long.MaxValue - 1 };
        for (int i = 0; i < 100_000; i++)
        {
            long raw = i < extremes.Length ? extremes[i] : unchecked((long)generator.NextUInt64());
            Fixed64 angle = Fixed64.FromRaw(raw);
            long sin = FixMath.Sin(angle).RawValue, cos = FixMath.Cos(angle).RawValue;
            if (Math.Abs(sin) > Fixed64.One.RawValue || Math.Abs(cos) > Fixed64.One.RawValue)
                Assert.Fail($"raw {raw}: out of [-1,1]: sin={sin}, cos={cos}");
            if (sin != FixMath.Sin(angle).RawValue || cos != FixMath.Cos(angle).RawValue)
                Assert.Fail($"raw {raw}: repeated evaluation differs");
        }
    }

    [Test]
    public void QuantizedPeriodIsExactForInputsWhoseAdditionDoesNotOverflow()
    {
        for (long raw = -10_000; raw <= 10_000; raw += 17)
        {
            Assert.That(FixMath.Sin(Fixed64.FromRaw(raw + FixMath.TwoPi.RawValue)), Is.EqualTo(FixMath.Sin(Fixed64.FromRaw(raw))));
            Assert.That(FixMath.Cos(Fixed64.FromRaw(raw + FixMath.TwoPi.RawValue)), Is.EqualTo(FixMath.Cos(Fixed64.FromRaw(raw))));
        }
    }
}
