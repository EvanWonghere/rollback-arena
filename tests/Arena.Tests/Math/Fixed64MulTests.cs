using System.Collections.Generic;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

/// <summary>
/// V1：用户实现的 Fixed64 乘法与 BigInteger 参考实现比对，作为默认验收的一部分。
/// </summary>
public class Fixed64MulTests
{
    [TestCaseSource(typeof(Fixed64MulCases), nameof(Fixed64MulCases.All))]
    public void MulMatchesBoundaryCase(long a, long b, long expected)
    {
        long actual = Fixed64.Mul(Fixed64.FromRaw(a), Fixed64.FromRaw(b)).RawValue;
        Assert.That(actual, Is.EqualTo(expected), Describe(a, b, expected, actual));
    }

    [TestCaseSource(typeof(Fixed64MulCases), nameof(Fixed64MulCases.All))]
    public void MulOperatorMatchesBoundaryCase(long a, long b, long expected)
    {
        long actual = (Fixed64.FromRaw(a) * Fixed64.FromRaw(b)).RawValue;
        Assert.That(actual, Is.EqualTo(expected), Describe(a, b, expected, actual));
    }

    [Test]
    public void MulMatchesReferenceOnRandomCases()
    {
        var generator = new Fixed64CaseGenerator();
        var failures = new List<string>();
        int mismatches = 0;
        for (int i = 0; i < Fixed64CaseGenerator.RandomCaseCount; i++)
        {
            var (a, b) = generator.NextPair();
            long expected = Fixed64Reference.Mul(a, b);
            long actual = Fixed64.Mul(Fixed64.FromRaw(a), Fixed64.FromRaw(b)).RawValue;
            if (actual == expected)
                continue;
            mismatches++;
            if (failures.Count < 10)
                failures.Add($"#{i}: {Describe(a, b, expected, actual)}");
        }

        Assert.That(mismatches, Is.Zero,
            $"种子 0x{Fixed64CaseGenerator.DefaultSeed:X}，{Fixed64CaseGenerator.RandomCaseCount} 组里有 {mismatches} 组不一致，前几组：\n  "
            + string.Join("\n  ", failures));
    }

    static string Describe(long a, long b, long expected, long actual) =>
        $"0x{a:X16} × 0x{b:X16}（{Fixed64.FromRaw(a)} × {Fixed64.FromRaw(b)}）"
        + $"：期望 0x{expected:X16}（{Fixed64.FromRaw(expected)}），实际 0x{actual:X16}（{Fixed64.FromRaw(actual)}）";
}
