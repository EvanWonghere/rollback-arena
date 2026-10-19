using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

/// <summary>
/// 补充会话的黑盒边界：观察公开进度、请求和真正执行后的状态，
/// 不读取私有环形槽，也不自行替会话生成纠正请求。
/// </summary>
public class RollbackSessionEdgeTests
{
    sealed class Harness
    {
        public readonly RollbackSession Session;
        public readonly RollbackRequestExecutor Executor;
        public readonly RollbackRequestBuffer Requests;

        public Harness(SessionOptions options, int localSlot = 0)
        {
            Session = new RollbackSession(options, localSlot);
            GameState initial = ArenaGame.Create();
            // 直接进入 Fight，避免倒计时忽略移动输入而掩盖错误预测。
            initial.Phase = MatchPhase.Fight;
            Executor = new RollbackRequestExecutor(initial, options.HistoryCapacity);
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

        public PlayerInput UsedRemoteAt(long frame)
        {
            for (int i = 0; i < Requests.Count; i++)
            {
                RollbackRequest request = Requests[i];
                if (request.Kind == RollbackRequestKind.Advance && request.Frame == frame)
                    return Session.LocalSlot == 0 ? request.Inputs.Player2 : request.Inputs.Player1;
            }
            Assert.Fail($"No Advance request for input frame {frame}.");
            return default;
        }

        public void RetireConfirmedExecutorHistory()
        {
            // 执行器与会话各管自己的历史；这里只退休已执行且已确认的状态。
            long confirmedState = System.Math.Min(Session.NextFrame, Session.LastConfirmedInputFrame + 1);
            Executor.RetireBefore(confirmedState);
        }
    }

    [Test]
    public void MissingRemoteRepeatsPreviousTimelineValueRatherThanFutureActual()
    {
        var h = new Harness(new SessionOptions(0));
        GameState expected = h.Executor.State;
        h.Session.ReceiveRemoteInput(0, PlayerInput.Right);
        h.Session.ReceiveRemoteInput(3, PlayerInput.Left);

        for (int frame = 0; frame < 3; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            Assert.That(h.UsedRemoteAt(frame), Is.EqualTo(PlayerInput.Right),
                "The actual input for future frame 3 cannot predict missing frames 1 or 2.");
            ArenaGame.Step(ref expected, new FrameInputs(PlayerInput.None, PlayerInput.Right));
        }

        Assert.That(h.Executor.State, Is.EqualTo(expected));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.Zero);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.Zero);
    }

    [Test]
    public void MatchingNonzeroLateActualDoesNotGenerateLoadOrResimulation()
    {
        var h = new Harness(new SessionOptions(0));
        h.Session.ReceiveRemoteInput(0, PlayerInput.Right);
        for (int frame = 0; frame < 3; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        }

        h.Session.ReceiveRemoteInput(1, PlayerInput.Right);
        h.Session.ReceiveRemoteInput(2, PlayerInput.Right);
        AdvanceResult result = h.Advance(); // 没有新的本地输入，只检查是否错误地产生纠正。

        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.NoLocalInput));
        Assert.That(h.Requests.Count, Is.Zero);
        Assert.That(h.Executor.ExecutedLoads, Is.Zero);
        Assert.That(h.Executor.ExecutedResimulations, Is.Zero);
        Assert.That(h.Session.Statistics.Rollbacks, Is.Zero);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EitherArrivalOrderCanCloseConfirmationGap(bool remoteFirst)
    {
        var session = new RollbackSession(new SessionOptions(0), 0);
        if (remoteFirst)
        {
            session.ReceiveRemoteInput(0, PlayerInput.None);
            Assert.That(session.LastConfirmedInputFrame, Is.EqualTo(-1));
            session.CaptureLocalInput(0, PlayerInput.None);
        }
        else
        {
            session.CaptureLocalInput(0, PlayerInput.None);
            Assert.That(session.LastConfirmedInputFrame, Is.EqualTo(-1));
            session.ReceiveRemoteInput(0, PlayerInput.None);
        }

        Assert.That(session.LastConfirmedInputFrame, Is.Zero);
        Assert.That(session.RemoteContiguousInputFrame, Is.Zero);
        Assert.That(session.NextFrame, Is.Zero, "Receiving input does not execute a game step.");
    }

    [Test]
    public void ActualForCurrentFrameCannotBypassEarlierGapAtPredictionLimit()
    {
        var h = new Harness(new SessionOptions(0, 2));
        for (int frame = 0; frame < 2; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        }

        h.Session.CaptureLocalInput(2, PlayerInput.None);
        h.Session.ReceiveRemoteInput(2, PlayerInput.Right);
        AdvanceResult result = h.Advance();

        Assert.That(result.ForwardAdvanced, Is.False);
        Assert.That(result.StallReason, Is.EqualTo(StallReason.PredictionLimit));
        Assert.That(h.Session.NextFrame, Is.EqualTo(2));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(-1));
        Assert.That(h.Requests.Count, Is.Zero);
        Assert.That(h.Executor.ExecutedLoads, Is.Zero, "Frame 2 has not been simulated, so it is not a mismatch.");
    }

    [Test]
    public void UndersizedCorrectionBatchFailsBeforeAppendingPartialRequests()
    {
        var h = new Harness(new SessionOptions(0, 4));
        for (int frame = 0; frame < 3; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        }
        h.Session.ReceiveRemoteInput(0, PlayerInput.Right);

        // 纠正需要 Load + 三组 Save/Step，总共七个请求；不足时不能留下半批。
        var tooSmall = new RollbackRequestBuffer(6);
        RollbackStatistics before = h.Session.Statistics;
        RollbackException error = Assert.Throws<RollbackException>(() => h.Session.AdvanceFrame(tooSmall))!;
        Assert.That(error.Error, Is.EqualTo(RollbackError.BufferTooSmall));
        Assert.That(tooSmall.Count, Is.Zero);
        Assert.That(h.Session.NextFrame, Is.EqualTo(3));
        Assert.That(h.Session.Statistics, Is.EqualTo(before));
        // 异常后会话结束；契约不承诺换用更大缓冲后可以继续调用。
    }

    [TestCase(4)]
    [TestCase(1000)]
    public void RejectedFutureInputCannotOccupyNormalFrameSlot(long futureFrame)
    {
        var h = new Harness(new SessionOptions(0, 1, 4, 5));
        RollbackException error = Assert.Throws<RollbackException>(() => h.Session.ReceiveRemoteInput(futureFrame, PlayerInput.Left))!;
        Assert.That(error.Error, Is.EqualTo(RollbackError.HistoryOverflow));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(-1));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(-1));
        Assert.That(h.Session.NextFrame, Is.Zero);
        Assert.That(h.Session.RemoteInputHistory.TryGet(futureFrame, out _), Is.False);
        Assert.That(h.Session.RemoteInputHistory.TryGet(0, out _), Is.False);

        // 异常会话不再使用；用独立会话证明正常帧 0 的对照操作可执行。
        var control = new Harness(new SessionOptions(0, 1, 4, 5));
        control.Session.CaptureLocalInput(0, PlayerInput.None);
        Assert.That(control.Session.ReceiveRemoteInput(0, PlayerInput.Right), Is.EqualTo(InputReceiveResult.Stored));
        Assert.That(control.Advance().ForwardAdvanced, Is.True);
        Assert.That(control.UsedRemoteAt(0), Is.EqualTo(PlayerInput.Right));
    }

    [Test]
    public void MultipleRingCyclesIgnoreRetiredInputsButStillDetectRetainedConflicts()
    {
        var h = new Harness(new SessionOptions(0, 1, 4, 5));
        for (int frame = 0; frame < 24; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            h.Session.ReceiveRemoteInput(frame, PlayerInput.Right);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            h.RetireConfirmedExecutorHistory();
        }

        ulong before = StateChecksum.Compute(h.Executor.State);
        Assert.That(h.Session.ReceiveRemoteInput(0, PlayerInput.Left), Is.EqualTo(InputReceiveResult.IgnoredRetired));
        Assert.That(h.Session.ReceiveRemoteInput(23, PlayerInput.Right), Is.EqualTo(InputReceiveResult.Duplicate));
        RollbackException conflict = Assert.Throws<RollbackException>(() => h.Session.ReceiveRemoteInput(23, PlayerInput.Left))!;
        Assert.That(conflict.Error, Is.EqualTo(RollbackError.ConflictingInput));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(23));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(23));
        Assert.That(h.Executor.State.Frame, Is.EqualTo(24));
        Assert.That(StateChecksum.Compute(h.Executor.State), Is.EqualTo(before));
    }

    [Test]
    public void PredictionKeepsPreviousInputSeedAfterSeveralHistoryRetirements()
    {
        var h = new Harness(new SessionOptions(0, 2, 5, 7));
        for (int frame = 0; frame < 15; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            h.Session.ReceiveRemoteInput(frame, PlayerInput.Right);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            h.RetireConfirmedExecutorHistory();
        }

        h.Session.CaptureLocalInput(15, PlayerInput.None);
        Assert.That(h.Advance().ForwardAdvanced, Is.True);
        Assert.That(h.UsedRemoteAt(15), Is.EqualTo(PlayerInput.Right),
            "Retiring confirmed history must preserve the predecessor needed for prediction.");
        Assert.That(h.Executor.ExecutedLoads, Is.Zero);
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(14));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(4)]
    public void PlayerTwoLateCorrectionPreservesGlobalSlotsAndSingleDelayPrefix(int delay)
    {
        var h = new Harness(new SessionOptions(delay), localSlot: 1);
        GameState expected = h.Executor.State;
        const int captures = 6;
        for (int captureFrame = 0; captureFrame < captures; captureFrame++)
        {
            h.Session.CaptureLocalInput(captureFrame, PlayerInput.None);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        }

        // 远端是全局 P1。这里只交付一次延迟后的目标帧，不能再加 delay。
        for (int captureFrame = 0; captureFrame < captures; captureFrame++)
            h.Session.ReceiveRemoteInput(captureFrame + delay, PlayerInput.Right);

        // 捕获结束后仍有已登记的本地延迟尾巴；终点没有新的本地输入。
        while (h.Session.NextFrame < captures + delay)
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
        AdvanceResult endpoint = h.Advance();
        Assert.That(endpoint.ForwardAdvanced, Is.False);
        Assert.That(endpoint.StallReason, Is.EqualTo(StallReason.NoLocalInput));

        for (int frame = 0; frame < captures + delay; frame++)
            ArenaGame.Step(ref expected, new FrameInputs(
                frame < delay ? PlayerInput.None : PlayerInput.Right,
                PlayerInput.None));

        Assert.That(h.Executor.State, Is.EqualTo(expected));
        Assert.That(h.Executor.ExecutedLoads, Is.GreaterThan(0), "The missing remote inputs were genuinely mispredicted.");
        Assert.That(h.Session.Statistics.Rollbacks, Is.EqualTo(h.Executor.ExecutedLoads));
        Assert.That(h.Session.NextFrame, Is.EqualTo(captures + delay));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(captures + delay - 1));
    }

    [Test]
    public void MovingReceiveWindowRejectsItsUpperBoundaryWithoutPollutingRetainedInput()
    {
        var h = new Harness(new SessionOptions(0, 1, 4, 5));
        for (int frame = 0; frame < 12; frame++)
        {
            h.Session.CaptureLocalInput(frame, PlayerInput.None);
            h.Session.ReceiveRemoteInput(frame, PlayerInput.Right);
            Assert.That(h.Advance().ForwardAdvanced, Is.True);
            h.RetireConfirmedExecutorHistory();
        }

        // 完成帧 11 后，保留前一帧作为预测种子，窗口为 [11, 15)。
        // 帧 15 与仍保留的帧 11 同槽，不能先写入再报告溢出。
        RollbackException error = Assert.Throws<RollbackException>(
            () => h.Session.ReceiveRemoteInput(15, PlayerInput.Left))!;
        Assert.That(error.Error, Is.EqualTo(RollbackError.HistoryOverflow));
        Assert.That(h.Session.RemoteInputHistory.TryGet(15, out _), Is.False);
        Assert.That(h.Session.RemoteInputHistory.TryGet(11, out PlayerInput retained), Is.True);
        Assert.That(retained, Is.EqualTo(PlayerInput.Right));
        Assert.That(h.Session.NextFrame, Is.EqualTo(12));
        Assert.That(h.Session.RemoteContiguousInputFrame, Is.EqualTo(11));
        Assert.That(h.Session.LastConfirmedInputFrame, Is.EqualTo(11));
    }

    [Test]
    public void RemoteFrameWithoutRepresentableSuccessorIsRejectedBeforeMutation()
    {
        var session = new RollbackSession(new SessionOptions(0), 0);
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => session.ReceiveRemoteInput(long.MaxValue, PlayerInput.Right));
        Assert.That(session.NextFrame, Is.Zero);
        Assert.That(session.RemoteContiguousInputFrame, Is.EqualTo(-1));
        Assert.That(session.LastConfirmedInputFrame, Is.EqualTo(-1));
        Assert.That(session.RemoteInputHistory.TryGet(long.MaxValue, out _), Is.False);
    }

    [TestCase(0, long.MaxValue)]
    [TestCase(2, long.MaxValue - 2)]
    [TestCase(2, long.MaxValue - 1)]
    [TestCase(2, long.MaxValue)]
    public void CaptureRejectsUnrepresentableTargetOrSuccessorBeforeMutation(int delay, long captureFrame)
    {
        var session = new RollbackSession(new SessionOptions(delay), 0);
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => session.CaptureLocalInput(captureFrame, PlayerInput.Right));
        Assert.That(session.NextFrame, Is.Zero);
        Assert.That(session.LastConfirmedInputFrame, Is.EqualTo(delay - 1));
        Assert.That(session.RemoteContiguousInputFrame, Is.EqualTo(delay - 1));
        Assert.That(session.LocalInputHistory.TryGet(long.MaxValue, out _), Is.False);
        Assert.That(session.LocalInputHistory.TryGet(delay, out _), Is.False);
    }
}
