using System;
using System.IO;
using Arena.Headless;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

public class EquivalenceHarnessTests
{
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(4)]
    public void OfflineOracleAppliesDelayOnceAndRequiresEveryConfirmation(int delay)
    {
        var capture = new FrameInputs[100];
        capture[90] = new FrameInputs(PlayerInput.Right, PlayerInput.None);
        var trace = new EquivalenceTrace(capture, delay);
        Assert.That(trace.EndFrame, Is.EqualTo(100 + delay));
        Assert.That(trace.FightInputEdges, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => trace.RequireComplete());
        for (int peer = 0; peer < 2; peer++)
        {
            var state = ArenaGame.Create();
            trace.Observe(peer, 0, StateChecksum.Compute(state), state);
            for (int f = 0; f < trace.EndFrame; f++)
            {
                ArenaGame.Step(ref state, f < delay ? default : capture[f - delay]);
                trace.Observe(peer, f + 1, StateChecksum.Compute(state), state);
            }
        }
        Assert.DoesNotThrow(() => trace.RequireComplete());
    }

    [Test]
    public void OracleRejectsWrongHashMissingFramesAndReportsFieldDifference()
    {
        var trace = new EquivalenceTrace(new FrameInputs[100], 0);
        var state = ArenaGame.Create();
        trace.Observe(0, 0, StateChecksum.Compute(state), state);
        var wrong = state;
        ArenaGame.Step(ref wrong, default);
        wrong.Player1.X += Fixed64.One;
        var error = Assert.Throws<InvalidOperationException>(() => trace.Observe(0, 1, StateChecksum.Compute(wrong), wrong));
        Assert.That(error!.Message, Does.Contain("state.Player1.X"));
        state.Frame = 2;
        Assert.Throws<InvalidOperationException>(() => trace.Observe(0, 2, StateChecksum.Compute(state), state));
        Assert.Throws<InvalidOperationException>(() => trace.RequireComplete());
    }

    [Test]
    public void CorpusIsReproducibleAndContainsNonzeroFightInputs()
    {
        var first = RollbackEquivalenceRunner.Corpus(0, 2400);
        var second = RollbackEquivalenceRunner.Corpus(0, 2400);
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first[90].Player1, Is.Not.EqualTo(PlayerInput.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => RollbackEquivalenceRunner.Corpus(0, 0));
    }

    [Test]
    public void SyncTestCliRejectsInvalidDistance()
    {
        TextWriter oldError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.That(Program.Main(new[] { "--sync-test", "--distance", "0" }), Is.EqualTo(2));
            Assert.That(error.ToString(), Is.Not.Empty);
        }
        finally { Console.SetError(oldError); }
    }

    [TestCase(10)]
    [TestCase(90)]
    public void ReceiveWithoutExecutingCorrectionDoesNotPublishOrRetire(int changedFrame)
    {
        var captured = new FrameInputs[100];
        captured[changedFrame] = new FrameInputs(PlayerInput.Right, PlayerInput.None);
        var trace = new EquivalenceTrace(captured, 0);
        var executor = new RollbackRequestExecutor(ArenaGame.Create(), 128);
        trace.Observe(0, 0, StateChecksum.Compute(executor.State), executor.State);
        for (int f = 0; f <= changedFrame; f++)
        {
            executor.Execute(RollbackRequest.Save(f));
            executor.Execute(RollbackRequest.Step(f, default));
        }
        trace.PublishAfterBatch(0, changedFrame - 1, executor, true);
        trace.PublishAfterBatch(0, changedFrame, executor, false);
        Assert.That(trace.ConfirmedThrough(0), Is.EqualTo(changedFrame));
        Assert.That(executor.TryGetState(changedFrame, out _), Is.True, "Correction snapshot must remain available.");
        executor.Execute(RollbackRequest.Load(changedFrame));
        executor.Execute(RollbackRequest.Save(changedFrame));
        executor.Execute(RollbackRequest.Step(changedFrame, captured[changedFrame], true));
        trace.PublishAfterBatch(0, changedFrame, executor, true);
        Assert.That(trace.ConfirmedThrough(0), Is.EqualTo(changedFrame + 1));
    }

    [TestCase(true, StallReason.None, false)]
    [TestCase(false, StallReason.PredictionLimit, false)]
    [TestCase(false, StallReason.NoLocalInput, true)]
    public void EndpointRequiresObservedNoLocalInputAndNoExtraStep(bool forward, StallReason reason, bool extraStep)
    {
        var trace = new EquivalenceTrace(new FrameInputs[100], 0);
        var requests = new RollbackRequestBuffer(32);
        Assert.That(trace.CheckEndpointBatch(99, new AdvanceResult(true), requests), Is.False);
        Assert.That(trace.CheckEndpointBatch(100, new AdvanceResult(false, StallReason.NoLocalInput), requests), Is.True);
        if (extraStep) requests.Add(RollbackRequest.Step(100, default));
        Assert.Throws<InvalidOperationException>(() => trace.CheckEndpointBatch(100, new AdvanceResult(forward, reason), requests));
    }

    [Test]
    public void OracleRejectsAStaleCachedHashEvenIfItEqualsExpectedHash()
    {
        var trace = new EquivalenceTrace(new FrameInputs[100], 0);
        var state = ArenaGame.Create();
        trace.Observe(0, 0, StateChecksum.Compute(state), state);
        ArenaGame.Step(ref state, default);
        ulong cached = StateChecksum.Compute(state);
        state.Player1.X += Fixed64.One;
        Assert.That(Assert.Throws<InvalidOperationException>(() => trace.Observe(0, 1, cached, state))!.Message, Does.Contain("Cached hash"));
    }
}
