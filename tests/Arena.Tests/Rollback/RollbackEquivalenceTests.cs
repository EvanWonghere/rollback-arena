using System;
using System.Collections.Generic;
using Arena.Headless;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

public class RollbackEquivalenceTests
{
    public static IEnumerable<TestCaseData> Seeds()
    {
        string? text = Environment.GetEnvironmentVariable("ARENA_V5_SEEDS");
        int count = text == null ? 16 : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (count <= 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "ARENA_V5_SEEDS must be 1..1000.");
        for (int seed = 0; seed < count; seed++) yield return new TestCaseData((ulong)seed).SetArgDisplayNames($"seed={seed}");
    }

    [TestCaseSource(nameof(Seeds))]
    public void ConfirmedFramesEqualIndependentOfflineOracle(ulong seed)
    {
        EquivalenceRun run = RollbackEquivalenceRunner.Run(seed);
        Assert.That(run.Frames, Is.GreaterThanOrEqualTo(2400));
        Assert.That(run.FightFrames, Is.GreaterThan(0));
        Assert.That(run.MaxRollbackDistance, Is.LessThanOrEqualTo(8));
        TestContext.Out.WriteLine($"seed={seed} frames={run.Frames} ticks={run.NetworkTicks} loads={run.Loads} resim={run.ResimulatedFrames} stalls={run.Stalls} fight={run.FightFrames} inputEdges={run.FightInputEdges} sent={run.Network.Sent} delivered={run.Network.Delivered} maxRollback={run.MaxRollbackDistance} dropped={run.Network.Dropped} duplicated={run.Network.Duplicated} hash={run.FinalHash:X16}");
    }
}
