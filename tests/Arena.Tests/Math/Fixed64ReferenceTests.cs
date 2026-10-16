using NUnit.Framework;

namespace Arena.Tests.FixedMath;

/// <summary>
/// 检查测试本身：参考实现要和手算的期望值一致，随机用例要真的覆盖到各种情况。
/// 这些测试不碰 Fixed64，所以乘法还没实现时它们也应该是绿的。
/// </summary>
public class Fixed64ReferenceTests
{
    [TestCaseSource(typeof(Fixed64MulCases), nameof(Fixed64MulCases.All))]
    public void ReferenceMulMatchesHandComputed(long a, long b, long expected)
    {
        Assert.That(Fixed64Reference.Mul(a, b), Is.EqualTo(expected));
    }

    [Test]
    public void GeneratorIsReproducible()
    {
        var first = new Fixed64CaseGenerator();
        var second = new Fixed64CaseGenerator();
        for (int i = 0; i < 1000; i++)
            Assert.That(second.NextPair(), Is.EqualTo(first.NextPair()));
    }

    [Test]
    public void RandomMulCasesCoverEveryOutcome()
    {
        var generator = new Fixed64CaseGenerator();
        int exact = 0, saturatedHigh = 0, saturatedLow = 0, ties = 0, negativeTimesNegative = 0;
        for (int i = 0; i < Fixed64CaseGenerator.RandomCaseCount; i++)
        {
            var (a, b) = generator.NextPair();
            long product = Fixed64Reference.Mul(a, b);
            if (product == long.MaxValue) saturatedHigh++;
            else if (product == long.MinValue) saturatedLow++;
            else
            {
                exact++;
                if (Fixed64Reference.IsMulTie(a, b)) ties++;
            }
            if (a < 0 && b < 0) negativeTimesNegative++;
        }

        // 每一类至少占 1%（1 万组），否则说明生成器的分布偏了。
        const int atLeast = Fixed64CaseGenerator.RandomCaseCount / 100;
        Assert.Multiple(() =>
        {
            Assert.That(exact, Is.GreaterThan(atLeast), "不饱和的乘积");
            Assert.That(saturatedHigh, Is.GreaterThan(atLeast), "饱和到 MaxValue");
            Assert.That(saturatedLow, Is.GreaterThan(atLeast), "饱和到 MinValue");
            Assert.That(ties, Is.GreaterThan(atLeast), "不饱和且恰好一半");
            Assert.That(negativeTimesNegative, Is.GreaterThan(atLeast), "负×负");
        });
    }

    [Test]
    public void RandomAddSubCasesCoverSaturation()
    {
        var generator = new Fixed64CaseGenerator();
        int addHigh = 0, addLow = 0, subHigh = 0, subLow = 0;
        for (int i = 0; i < Fixed64CaseGenerator.RandomCaseCount; i++)
        {
            var (a, b) = generator.NextPair();
            long sum = Fixed64Reference.Add(a, b), diff = Fixed64Reference.Sub(a, b);
            if (sum == long.MaxValue) addHigh++;
            if (sum == long.MinValue) addLow++;
            if (diff == long.MaxValue) subHigh++;
            if (diff == long.MinValue) subLow++;
        }

        const int atLeast = Fixed64CaseGenerator.RandomCaseCount / 100;
        Assert.Multiple(() =>
        {
            Assert.That(addHigh, Is.GreaterThan(atLeast), "加法饱和到 MaxValue");
            Assert.That(addLow, Is.GreaterThan(atLeast), "加法饱和到 MinValue");
            Assert.That(subHigh, Is.GreaterThan(atLeast), "减法饱和到 MaxValue");
            Assert.That(subLow, Is.GreaterThan(atLeast), "减法饱和到 MinValue");
        });
    }
}
