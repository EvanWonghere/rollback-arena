using System.Collections.Generic;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.FixedMath;

/// <summary>
/// V1：Fixed64 乘法和 BigInteger 参考实现比对。乘法由人实现（方案 S1），在那之前这些测试都是红的，
/// 而且只应该因为 NotImplementedException 而红。人实现乘法之后，去掉 HumanTodo 分类和 .ai/verify.sh 里的排除。
/// </summary>
[Category("HumanTodo")]
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
