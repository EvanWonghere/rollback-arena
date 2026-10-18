using System;
using System.Numerics;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

public class FixVector3Tests
{
    const long One = 1L << 32;
    static FixVector3 V(long x, long y, long z) => new(Fixed64.FromRaw(x), Fixed64.FromRaw(y), Fixed64.FromRaw(z));

    [Test]
    public void ConstructionEqualityAndAxes()
    {
        var value = V(1, -2, 3);
        Assert.Multiple(() =>
        {
            Assert.That(default(FixVector3), Is.EqualTo(FixVector3.Zero));
            Assert.That(value.X.RawValue, Is.EqualTo(1));
            Assert.That(value.Y.RawValue, Is.EqualTo(-2));
            Assert.That(value.Z.RawValue, Is.EqualTo(3));
            Assert.That(value == V(1, -2, 3), Is.True);
            Assert.That(value != V(1, -2, 4), Is.True);
            Assert.That(value.Equals((object)V(1, -2, 3)), Is.True);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals((object)1), Is.False);
            Assert.That(value.GetHashCode(), Is.EqualTo(V(1, -2, 3).GetHashCode()));
            Assert.That(FixVector3.UnitX + FixVector3.UnitY + FixVector3.UnitZ, Is.EqualTo(V(One, One, One)));
            Assert.That(FixVector3.Dot(FixVector3.UnitX, FixVector3.UnitY), Is.EqualTo(Fixed64.Zero));
            Assert.That(FixVector3.UnitX.LengthSquared, Is.EqualTo(Fixed64.One));
        });
    }

    [Test]
    public void ZeroAndTinyVectorsHaveDefinedNormalization()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FixVector3.Zero.Length, Is.EqualTo(Fixed64.Zero));
            Assert.That(FixVector3.Zero.Normalized, Is.EqualTo(FixVector3.Zero));
            Assert.That(V(1, 0, 0).Normalized, Is.EqualTo(FixVector3.UnitX));
            Assert.That(V(0, -1, 0).Normalized, Is.EqualTo(-FixVector3.UnitY));
            Assert.That(V(0, 0, 1).Length, Is.EqualTo(Fixed64.Epsilon));
        });
        Assert.Throws<DivideByZeroException>(() => _ = FixVector3.Zero / Fixed64.Zero);
    }

    [Test]
    public void IndependentVectorReferenceMatchesHandComputedVectors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExtendedMathReference.Vector(0, 0, 0), Is.EqualTo((0L, 0L, 0L, 0L)));
            Assert.That(ExtendedMathReference.Vector(1, 0, 0), Is.EqualTo((1L, One, 0L, 0L)));
            Assert.That(ExtendedMathReference.Vector(-1, 0, 0), Is.EqualTo((1L, -One, 0L, 0L)));
            var expected = ExtendedMathReference.Vector(3 * One, 4 * One, 0);
            Assert.That(expected.Length, Is.EqualTo(5 * One));
            Assert.That(expected.X, Is.EqualTo(2576980378L));
            Assert.That(expected.Y, Is.EqualTo(3435973837L));
            Assert.That(expected.Z, Is.Zero);
        });
    }

    [Test]
    public void LargeAndExtremeVectorsMeetTheDeclaredErrorBound()
    {
        (long X, long Y, long Z)[] cases =
        {
            (3 * One, 4 * One, 0), (1, 1, 1), (1, long.MaxValue, 0),
            (1_000_000_000L * One, 1_000_000_000L * One, 0),
            (long.MinValue, 0, 0), (long.MaxValue, long.MinValue, long.MaxValue),
            (long.MinValue, long.MinValue, long.MinValue),
            (long.MaxValue - 1, One, 1), (long.MaxValue, 0, 0),
        };
        foreach (var (x, y, z) in cases) CheckGeometry(x, y, z, out _, out _);
        Assert.That(V(long.MaxValue, 0, 0).LengthSquared, Is.EqualTo(Fixed64.MaxValue));
    }

    [Test]
    public void MillionVectorsMeetIndependentReferenceAndScalarOperationSemantics()
    {
        var generator = new Fixed64CaseGenerator();
        double worstRelativeLength = 0;
        long worstNormalizedRaw = 0;
        int tiny = 0, mixed = 0, huge = 0;
        for (int i = 0; i < 1_000_000; i++)
        {
            long x = generator.NextOperand(), y = generator.NextOperand(), z = generator.NextOperand();
            if (i % 4 == 0)
            {
                x = (long)(generator.NextUInt64() % 17) - 8;
                y = (long)(generator.NextUInt64() % 17) - 8;
                z = (long)(generator.NextUInt64() % 17) - 8;
                tiny++;
            }
            else if (i % 4 == 1)
            {
                x = long.MinValue + (long)(generator.NextUInt64() % 32);
                y = long.MaxValue - (long)(generator.NextUInt64() % 32);
                huge++;
            }
            else mixed++;

            CheckGeometry(x, y, z, out double relative, out long componentError);
            worstRelativeLength = Math.Max(worstRelativeLength, relative);
            worstNormalizedRaw = Math.Max(worstNormalizedRaw, componentError);

            FixVector3 a = V(x, y, z);
            long bx = generator.NextOperand(), by = generator.NextOperand(), bz = generator.NextOperand();
            FixVector3 b = V(bx, by, bz);
            long scalar = generator.NextOperand();
            if (scalar == 0) scalar = One;
            FixVector3 sum = a + b, difference = a - b, neg = -a;
            FixVector3 product = a * Fixed64.FromRaw(scalar), reverse = Fixed64.FromRaw(scalar) * a;
            FixVector3 quotient = a / Fixed64.FromRaw(scalar);
            if (sum != V(Fixed64Reference.Add(x, bx), Fixed64Reference.Add(y, by), Fixed64Reference.Add(z, bz))
                || difference != V(Fixed64Reference.Sub(x, bx), Fixed64Reference.Sub(y, by), Fixed64Reference.Sub(z, bz))
                || neg != V(Fixed64Reference.Negate(x), Fixed64Reference.Negate(y), Fixed64Reference.Negate(z))
                || product != V(Fixed64Reference.Mul(x, scalar), Fixed64Reference.Mul(y, scalar), Fixed64Reference.Mul(z, scalar))
                || reverse != product
                || quotient != V(ExtendedMathReference.Div(x, scalar), ExtendedMathReference.Div(y, scalar), ExtendedMathReference.Div(z, scalar)))
                Assert.Fail($"#{i}: component operation differs from scalar reference");

            long dot = Fixed64Reference.Add(Fixed64Reference.Add(Fixed64Reference.Mul(x, bx), Fixed64Reference.Mul(y, by)), Fixed64Reference.Mul(z, bz));
            if (FixVector3.Dot(a, b).RawValue != dot) Assert.Fail($"#{i}: ordered saturated dot differs");
            long squared = Fixed64Reference.Add(Fixed64Reference.Add(Fixed64Reference.Mul(x, x), Fixed64Reference.Mul(y, y)), Fixed64Reference.Mul(z, z));
            if (a.LengthSquared.RawValue != squared) Assert.Fail($"#{i}: length squared differs");
        }
        Assert.Multiple(() =>
        {
            Assert.That(tiny, Is.EqualTo(250_000));
            Assert.That(huge, Is.EqualTo(250_000));
            Assert.That(mixed, Is.EqualTo(500_000));
        });
        TestContext.Out.WriteLine($"Vector 1,000,000: max length error / max(1,length)={worstRelativeLength:R}; max normalized component raw error={worstNormalizedRaw}; tiny={tiny}, huge={huge}, mixed={mixed}");
    }

    static void CheckGeometry(long x, long y, long z, out double relativeLengthError, out long normalizedRawError)
    {
        var expected = ExtendedMathReference.Vector(x, y, z);
        FixVector3 actual = V(x, y, z);
        long length = actual.Length.RawValue;
        BigInteger difference = BigInteger.Abs((BigInteger)length - expected.Length);
        BigInteger scale = BigInteger.Max(One, expected.Length);
        // 独立理想值最后量化最多再引入半个 raw，给 1 raw 的 oracle 裕量。
        if (difference * One > 16 * scale + One)
            Assert.Fail($"({x},{y},{z}): length raw={length}, ideal={expected.Length}, error={difference}");
        FixVector3 normalized = actual.Normalized;
        normalizedRawError = Math.Max(Math.Abs(normalized.X.RawValue - expected.X),
            Math.Max(Math.Abs(normalized.Y.RawValue - expected.Y), Math.Abs(normalized.Z.RawValue - expected.Z)));
        if (normalizedRawError > 16)
            Assert.Fail($"({x},{y},{z}): normalized component raw error={normalizedRawError}");
        relativeLengthError = (double)difference / (double)scale;
    }
}
