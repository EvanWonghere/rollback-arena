using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

/// <summary>行为测试先红，用户实现调度后全部纳入默认检查；不是对空骨架的成功测试。</summary>
public class RollbackSessionContractTests
{
    sealed class Harness
    {
        public readonly RollbackSession Session;
        public readonly RollbackRequestExecutor Executor;
        public readonly RollbackRequestBuffer Requests;
        public Harness(SessionOptions options, int local = 0, bool fight = false)
        {
            Session = new RollbackSession(options, local);
            GameState state = ArenaGame.Create();
            if (fight) state.Phase = MatchPhase.Fight;
            Executor = new RollbackRequestExecutor(state, options.HistoryCapacity);
            Requests = new RollbackRequestBuffer(options.RequestCapacity);
        }
        public AdvanceResult Advance()
        {
            long before = Session.NextFrame;
            Requests.Clear();
            AdvanceResult result = Session.AdvanceFrame(Requests);
            Executor.Execute(Requests);
            Assert.That(Executor.State.Frame, Is.EqualTo(before + (result.ForwardAdvanced ? 1 : 0)));
            Assert.That(Session.NextFrame, Is.EqualTo(Executor.State.Frame));
            return result;
        }
        public FrameInputs Map(PlayerInput local, PlayerInput remote) => Session.LocalSlot == 0 ? new FrameInputs(local, remote) : new FrameInputs(remote, local);
    }

    [TestCase(0, 0)]
    [TestCase(0, 1)]
    [TestCase(2, 0)]
    [TestCase(2, 1)]
    [TestCase(4, 0)]
    [TestCase(4, 1)]
    public void ActualInputsDelayExactlyOnceAndPreserveGlobalSlots(int delay, int slot)
    {
        var h = new Harness(new SessionOptions(delay), slot, true);
        GameState expected = h.Executor.State;
        const int captures = 16;
        for (int c = 0; c < captures; c++)
        {
            FramedInput outgoing = h.Session.CaptureLocalInput(c, PlayerInput.Right);
            Assert.That(outgoing.Frame, Is.EqualTo(c + delay));
            Assert.That(outgoing.Value, Is.EqualTo(PlayerInput.Right));
            h.Session.ReceiveRemoteInput(c + delay, PlayerInput.Jump);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            ArenaGame.Step(ref expected, c < delay ? default : h.Map(PlayerInput.Right, PlayerInput.Jump));
            Assert.That(StateChecksum.Compute(h.Executor.State), Is.EqualTo(StateChecksum.Compute(expected)));
        }
        for (int f = captures; f < captures + delay; f++)
        {
            Assert.That(h.Advance().ForwardAdvanced, Is.True, "already captured delay tail must execute");
            ArenaGame.Step(ref expected, h.Map(PlayerInput.Right, PlayerInput.Jump));
        }
        Assert.That(h.Session.Statistics.Rollbacks, Is.Zero);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(captures + delay - 1));
        var result = h.Advance();
        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.NoLocalInput));
        Assert.That(h.Executor.State, Is.EqualTo(expected));
    }

    [Test]
    public void MissingLocalActualInputIsNeverPredicted()
    {
        var h = new Harness(new SessionOptions(0));
        h.Session.ReceiveRemoteInput(0, PlayerInput.Attack);
        var result = h.Advance();
        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.NoLocalInput));
        Assert.That(h.Executor.State.Frame, Is.Zero);
    }

    [Test]
    public void MatchingLateActualDoesNotRollback()
    {
        var h = new Harness(new SessionOptions(0));
        h.Session.CaptureLocalInput(0, PlayerInput.None);
        Assert.That(h.Advance().ForwardAdvanced, Is.True);
        h.Session.ReceiveRemoteInput(0, PlayerInput.None);
        h.Session.CaptureLocalInput(1, PlayerInput.None);
        h.Session.ReceiveRemoteInput(1, PlayerInput.None);
        Assert.That(h.Advance().ForwardAdvanced, Is.True);
        Assert.That(h.Session.Statistics.Rollbacks, Is.Zero);
        Assert.That(h.Executor.ExecutedLoads, Is.Zero);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(1));
    }

    [Test]
    public void EndpointLateCorrectionDoesNotAdvanceBeyondCapturedTail()
    {
        var h = new Harness(new SessionOptions(0), fight: true);
        GameState expected = h.Executor.State;
        for (int f = 0; f < 8; f++)
        {
            h.Session.CaptureLocalInput(f, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        }
        for (int f = 0; f < 8; f++)
        {
            h.Session.ReceiveRemoteInput(f, PlayerInput.Right);
            ArenaGame.Step(ref expected, new FrameInputs(PlayerInput.None, PlayerInput.Right));
        }
        AdvanceResult result = h.Advance();
        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.NoLocalInput));
        Assert.That(h.Requests[0].Kind, Is.EqualTo(RollbackRequestKind.LoadState));
        Assert.That(h.Requests[0].Frame, Is.Zero);
        Assert.That(h.Executor.ExecutedResimulations, Is.EqualTo(8));
        Assert.That(h.Executor.State, Is.EqualTo(expected));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(7));
    }

    [Test]
    public void EarliestMismatchAndFutureActualDoNotLeakIntoPastPrediction()
    {
        var h = new Harness(new SessionOptions(0), fight: true);
        GameState expected = h.Executor.State;
        for (int f = 0; f < 8; f++) { h.Session.CaptureLocalInput(f, PlayerInput.None); h.Advance(); }
        h.Session.ReceiveRemoteInput(3, PlayerInput.Jump);
        h.Session.ReceiveRemoteInput(0, PlayerInput.Right);
        h.Advance();
        Assert.That(h.Requests[0].Frame, Is.Zero);
        for (int f = 0; f < 8; f++) ArenaGame.Step(ref expected, new FrameInputs(PlayerInput.None, f < 3 ? PlayerInput.Right : PlayerInput.Jump));
        Assert.That(h.Executor.State, Is.EqualTo(expected));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.Zero, "frame 1 is missing");
    }

    [Test]
    public void FullPredictionWindowStillExecutesCorrectionOnlyBatch()
    {
        var h = new Harness(new SessionOptions(0, 2, 256, 32), fight: true);
        for (int f = 0; f < 2; f++) { h.Session.CaptureLocalInput(f, PlayerInput.None); Assert.That(h.Advance().ForwardAdvanced, Is.True); }
        h.Session.CaptureLocalInput(2, PlayerInput.None);
        h.Session.ReceiveRemoteInput(1, PlayerInput.Right); // gap at 0 keeps window full
        AdvanceResult result = h.Advance();
        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.PredictionLimit));
        Assert.That(h.Session.NextFrame, Is.EqualTo(2));
        Assert.That(h.Executor.ExecutedLoads, Is.EqualTo(1));
        Assert.That(h.Requests[0].Frame, Is.EqualTo(1));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(-1));
    }

    [Test]
    public void LockstepWaitsForCurrentRemoteActual()
    {
        var h = new Harness(new SessionOptions(0, 0));
        h.Session.CaptureLocalInput(0, PlayerInput.Right);
        AdvanceResult waiting = h.Advance();
        Assert.That(waiting.ForwardAdvanced, Is.False);
        Assert.That(waiting.StallReason, Is.EqualTo(StallReason.PredictionLimit));
        h.Session.ReceiveRemoteInput(0, PlayerInput.None);
        Assert.That(h.Advance().ForwardAdvanced, Is.True);
        Assert.That(h.Executor.ExecutedLoads, Is.Zero);
    }

    [Test]
    public void OutOfOrderActualDoesNotCrossConfirmationGap()
    {
        var h = new Harness(new SessionOptions(0));
        for (int f = 0; f < 3; f++) { h.Session.CaptureLocalInput(f, PlayerInput.None); h.Advance(); }
        h.Session.ReceiveRemoteInput(2, PlayerInput.Right);
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(-1));
        h.Session.ReceiveRemoteInput(0, PlayerInput.Left);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.Zero);
        h.Session.ReceiveRemoteInput(1, PlayerInput.Up);
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(2));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(2));
    }

    [Test]
    public void RepeatedCapturesAndPacketsAreIdempotentButConflictsFail()
    {
        var h = new Harness(new SessionOptions(0));
        h.Session.CaptureLocalInput(0, PlayerInput.Attack);
        Assert.That(h.Session.CaptureLocalInput(0, PlayerInput.Attack).Value, Is.EqualTo(PlayerInput.Attack));
        Assert.That(Assert.Throws<RollbackException>(() => h.Session.CaptureLocalInput(0, PlayerInput.Jump))!.Error, Is.EqualTo(RollbackError.ConflictingInput));
        Assert.That(h.Session.ReceiveRemoteInput(0, PlayerInput.Right), Is.EqualTo(InputReceiveResult.Stored));
        Assert.That(h.Session.ReceiveRemoteInput(0, PlayerInput.Right), Is.EqualTo(InputReceiveResult.Duplicate));
        Assert.That(Assert.Throws<RollbackException>(() => h.Session.ReceiveRemoteInput(0, PlayerInput.Left))!.Error, Is.EqualTo(RollbackError.ConflictingInput));
    }

    [Test]
    public void HeldAttackAcrossRecoveryRollbackDoesNotCreateAnotherPress()
    {
        var h = new Harness(new SessionOptions(0), fight: true);
        GameState expected = h.Executor.State;
        for (int f = 0; f < 31; f++)
        {
            h.Session.CaptureLocalInput(f, PlayerInput.Attack);
            if (f < 23) h.Session.ReceiveRemoteInput(f, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            ArenaGame.Step(ref expected, new FrameInputs(PlayerInput.Attack, f < 23 ? PlayerInput.None : PlayerInput.Right));
        }
        for (int f = 23; f < 31; f++) h.Session.ReceiveRemoteInput(f, PlayerInput.Right);
        h.Advance();
        Assert.That(h.Requests[0].Frame, Is.EqualTo(23));
        Assert.That(h.Executor.State, Is.EqualTo(expected));
        Assert.That(h.Executor.State.Player1.Action, Is.EqualTo(PlayerAction.Idle));
    }

    [Test]
    public void RetiredSlotsDoNotAliasNewFrames()
    {
        var h = new Harness(new SessionOptions(0, 1, 4, 5));
        for (int f = 0; f < 20; f++)
        {
            h.Session.CaptureLocalInput(f, PlayerInput.Right);
            h.Session.ReceiveRemoteInput(f, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            h.Executor.RetireBefore(f + 1);
        }
        ulong before = StateChecksum.Compute(h.Executor.State);
        Assert.That(h.Session.ReceiveRemoteInput(0, PlayerInput.Left), Is.EqualTo(InputReceiveResult.IgnoredRetired));
        Assert.That(StateChecksum.Compute(h.Executor.State), Is.EqualTo(before));
        Assert.Throws<RollbackException>(() => h.Session.ReceiveRemoteInput(1000, PlayerInput.None));
    }
}
