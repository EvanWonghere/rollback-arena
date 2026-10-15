using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Arena.Tests.Architecture.Fixtures;
using NUnit.Framework;

namespace Arena.Tests.Architecture;

/// <summary>V3 架构守卫：模拟层和回滚层不能出现会破坏确定性的用法。</summary>
public class DeterminismGuardTests
{
    static readonly Type[] GuardedAssemblies = { typeof(Arena.Sim.AssemblyMarker), typeof(Arena.Rollback.AssemblyMarker) };

    // 回滚层以后会依赖模拟层；除此之外只能引用 netstandard，这也挡住了 UnityEngine。
    static readonly HashSet<string> AllowedReferences = new() { "netstandard", "Arena.Sim" };

    [TestCaseSource(nameof(GuardedAssemblies))]
    public void GuardedAssemblyHasNoNondeterministicCode(Type marker)
    {
        var assembly = marker.Assembly;
        var result = DeterminismScanner.Scan(assembly.Location);

        Assert.That(result.TypesScanned, Is.GreaterThan(1), $"{assembly.GetName().Name} 里没有扫描到任何类型，守卫没有在检查东西");
        if (result.Violations.Count > 0)
            Assert.Fail($"{assembly.GetName().Name} 里有 {result.Violations.Count} 处会破坏确定性的用法：\n  "
                + string.Join("\n  ", result.Violations));
    }

    [TestCaseSource(nameof(GuardedAssemblies))]
    public void GuardedAssemblyOnlyReferencesAllowedAssemblies(Type marker)
    {
        using var pe = new PEReader(File.OpenRead(marker.Assembly.Location));
        var md = pe.GetMetadataReader();
        var references = md.AssemblyReferences.Select(r => md.GetString(md.GetAssemblyReference(r).Name)).ToList();

        Assert.That(references, Is.SubsetOf(AllowedReferences),
            $"{marker.Assembly.GetName().Name} 引用了不允许的程序集；模拟层和回滚层必须能原样放进 Unity 和其他运行时");
    }

    static IEnumerable<TestCaseData> BadFixtures()
    {
        yield return new TestCaseData(typeof(FloatField), "浮点");
        yield return new TestCaseData(typeof(DoubleLocal), "浮点");
        yield return new TestCaseData(typeof(MathSqrt), "浮点");
        yield return new TestCaseData(typeof(FloatInLambda), "浮点");
        yield return new TestCaseData(typeof(RandomUse), "禁用类型");
        yield return new TestCaseData(typeof(DateTimeUse), "禁用类型");
        yield return new TestCaseData(typeof(StopwatchUse), "禁用类型");
        yield return new TestCaseData(typeof(TickCountUse), "禁用成员");
        yield return new TestCaseData(typeof(DictionaryForeach), "无序遍历");
        yield return new TestCaseData(typeof(DictionaryKeys), "无序遍历");
        yield return new TestCaseData(typeof(DictionaryValuesForeach), "无序遍历");
        yield return new TestCaseData(typeof(HashSetForeach), "无序遍历");
        yield return new TestCaseData(typeof(NumericsVector), "浮点");
        yield return new TestCaseData(typeof(CryptoRandomUse), "禁用类型");
        yield return new TestCaseData(typeof(HashCodeUse), "禁用类型");
        yield return new TestCaseData(typeof(GuidUse), "禁用成员");
        yield return new TestCaseData(typeof(HashtableUse), "禁用类型");
        yield return new TestCaseData(typeof(ConcurrentDictionaryUse), "禁用类型");
        yield return new TestCaseData(typeof(HashSetCopyTo), "无序遍历");
        yield return new TestCaseData(typeof(HashSetRemoveWhere), "无序遍历");
        yield return new TestCaseData(typeof(ReadOnlyDictionaryKeys), "无序遍历");
    }

    [TestCaseSource(nameof(BadFixtures))]
    public void ScannerCatchesFixture(Type fixture, string rule)
    {
        var result = ScanFixture(fixture);

        Assert.That(result.TypesScanned, Is.GreaterThan(0));
        Assert.That(result.Violations.Select(v => v.Rule), Does.Contain(rule),
            "扫描结果：\n  " + string.Join("\n  ", result.Violations));
    }

    [Test]
    public void ScannerAcceptsAllowedCode()
    {
        var result = ScanFixture(typeof(Clean));

        Assert.That(result.MethodBodiesScanned, Is.GreaterThan(0));
        Assert.That(result.Violations, Is.Empty);
    }

    static DeterminismScanner.Result ScanFixture(Type fixture) =>
        DeterminismScanner.Scan(fixture.Assembly.Location, name => name == fixture.FullName);
}
