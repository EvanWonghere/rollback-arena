using System;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

public class RollbackInfrastructureTests
{
    [TestCase(-1, 8, 256, 32)]
    [TestCase(0, -1, 256, 32)]
    [TestCase(2, 8, 11, 32)]
    [TestCase(2, 8, 256, 18)]
    [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
    [TestCase(0, 0, 0, 3)]
    public void InvalidOptionsAreRejectedWithoutOverflow(int delay, int prediction, int history, int requests) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionOptions(delay, prediction, history, requests));

    [Test]
    public void OptionsAndSessionSkeletonPreserveConfigurationOnly()
    {
        var options = new SessionOptions();
        var session = new RollbackSession(options, 1);
        Assert.Multiple(() =>
        {
            Assert.That(options.InputDelayFrames, Is.EqualTo(2));
            Assert.That(options.MaxPredictionFrames, Is.EqualTo(8));
            Assert.That(options.HistoryCapacity, Is.EqualTo(256));
            Assert.That(options.RequestCapacity, Is.EqualTo(32));
            Assert.That(session.Options, Is.SameAs(options));
            Assert.That(session.LocalSlot, Is.EqualTo(1));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollbackSession(options, 2));
        Assert.Throws<ArgumentNullException>(() => new RollbackSession(null!, 0));
    }

    [Test]
    public void RingRequiresFullFrameTagAndExplicitRetirement()
    {
        var ring = new FrameRing<long>(2);
        ring.Store(0, 10);
        ring.Store(1, 20);
        Assert.That(ring.TryGet(2, out _), Is.False);
        Assert.That(Assert.Throws<RollbackException>(() => ring.Store(2, 30))!.Error, Is.EqualTo(RollbackError.HistoryOverflow));
        ring.Store(0, 11);
        Assert.That(ring.TryGet(0, out long old), Is.True);
        Assert.That(old, Is.EqualTo(11));
        ring.DiscardBefore(1);
        ring.Store(2, 30);
        Assert.That(ring.TryGet(0, out _), Is.False);
        Assert.That(ring.TryGet(2, out long current), Is.True);
        Assert.That(current, Is.EqualTo(30));
        ring.DiscardAfter(1);
        Assert.That(ring.TryGet(2, out _), Is.False);
        Assert.That(ring.TryGet(1, out _), Is.True);
    }

    [Test]
    public void InvalidFramesAndBufferCapacityAreExplicitErrors()
    {
        var ring = new FrameRing<long>(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Store(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.TryGet(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRing<long>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FramedInput(0, (PlayerInput)128));
        Assert.Throws<ArgumentOutOfRangeException>(() => RollbackRequest.Save(-1));
        var requests = new RollbackRequestBuffer(1);
        requests.Add(RollbackRequest.Save(0));
        Assert.That(Assert.Throws<RollbackException>(() => requests.Add(RollbackRequest.Save(1)))!.Error, Is.EqualTo(RollbackError.BufferTooSmall));
        Assert.That(requests.Count, Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = requests[1]);
        requests.Clear();
        Assert.That(requests.Count, Is.Zero);
    }

    [Test]
    public void ExecutorRunsOnlyTheProvidedOrderedRequests()
    {
        var state = ArenaGame.Create();
        state.Phase = MatchPhase.Fight;
        var executor = new RollbackRequestExecutor(state, 16);
        var right = new FrameInputs(PlayerInput.Right, PlayerInput.None);
        var left = new FrameInputs(PlayerInput.Left, PlayerInput.None);
        var requests = new RollbackRequestBuffer(8);
        requests.Add(RollbackRequest.Save(0));
        requests.Add(RollbackRequest.Step(0, right));
        requests.Add(RollbackRequest.Save(1));
        requests.Add(RollbackRequest.Step(1, right));
        executor.Execute(requests);
        ulong prior = StateChecksum.Compute(executor.State);
        Assert.That(executor.State.Frame, Is.EqualTo(2));
        Assert.That(executor.State.Player1.X, Is.EqualTo(state.Player1.X + GameplayRules.MoveSpeed * Fixed64.FromInt(2)));
        executor.Execute(RollbackRequest.Load(0));
        Assert.That(executor.TryGetHash(2, out _), Is.False, "old provisional hash must be removed");
        Assert.That(executor.TryGetState(1, out _), Is.False, "old provisional snapshot must be removed");
        for (int f = 0; f < 2; f++)
        {
            executor.Execute(RollbackRequest.Save(f));
            executor.Execute(RollbackRequest.Step(f, left, true));
        }
        Assert.That(executor.State.Player1.X, Is.EqualTo(state.Player1.X - GameplayRules.MoveSpeed * Fixed64.FromInt(2)));
        Assert.That(StateChecksum.Compute(executor.State), Is.Not.EqualTo(prior));
        Assert.That(executor.TryGetHash(2, out ulong hash), Is.True);
        Assert.That(hash, Is.EqualTo(StateChecksum.Compute(executor.State)));
        Assert.That(executor.ExecutedForwards, Is.EqualTo(2));
        Assert.That(executor.ExecutedResimulations, Is.EqualTo(2));
        Assert.That(executor.ExecutedLoads, Is.EqualTo(1));
        Assert.That(executor.MaxLoadDistance, Is.EqualTo(2));
    }

    [Test]
    public void ExecutorRejectsMissingSnapshotsAndFrameMismatch()
    {
        var executor = new RollbackRequestExecutor(ArenaGame.Create(), 4);
        Assert.That(Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Step(0, default)))!.Error, Is.EqualTo(RollbackError.MissingHistory));
        Assert.That(Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Load(0)))!.Error, Is.EqualTo(RollbackError.MissingHistory));
        Assert.That(Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Save(1)))!.Error, Is.EqualTo(RollbackError.FrameMismatch));
        executor.Execute(RollbackRequest.Save(0));
        executor.Execute(RollbackRequest.Step(0, default));
        executor.RetireBefore(1);
        Assert.That(executor.TryGetHash(0, out _), Is.False);
        Assert.That(Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Load(0)))!.Error, Is.EqualTo(RollbackError.MissingHistory));
    }

    [Test]
    public void ExecutorDoesNotTrustIncorrectResimulationLabels()
    {
        var executor = new RollbackRequestExecutor(ArenaGame.Create(), 4);
        executor.Execute(RollbackRequest.Save(0));
        Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Step(0, default, true)));
        executor.Execute(RollbackRequest.Step(0, default));
        executor.Execute(RollbackRequest.Load(0));
        Assert.Throws<RollbackException>(() => executor.Execute(RollbackRequest.Step(0, default)));
        executor.Execute(RollbackRequest.Step(0, default, true));
        Assert.That(executor.ExecutedResimulations, Is.EqualTo(1));
    }
}
