using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

/// <summary>实际输入推进行为；预测、纠正和退休由同目录的完整会话测试覆盖。</summary>
public class ActualInputAdvanceTests
{
    [Test]
    public void WaitingCountsStallsWithoutAdvancingOrProducingRequests()
    {
        var session = new RollbackSession(new SessionOptions(0, 0), 0);
        var requests = new RollbackRequestBuffer(32);

        AdvanceResult missingLocal = session.AdvanceFrame(requests);
        Assert.That(missingLocal.ForwardAdvanced, Is.False);
        Assert.That(missingLocal.StallReason, Is.EqualTo(StallReason.NoLocalInput));
        Assert.That(session.Statistics.Stalls, Is.EqualTo(1));
        Assert.That(session.NextFrame, Is.Zero);
        Assert.That(requests.Count, Is.Zero);

        session.CaptureLocalInput(0, PlayerInput.Right);
        AdvanceResult missingRemote = session.AdvanceFrame(requests);
        Assert.That(missingRemote.ForwardAdvanced, Is.False);
        Assert.That(missingRemote.StallReason, Is.EqualTo(StallReason.PredictionLimit));
        Assert.That(session.Statistics.Stalls, Is.EqualTo(2));
        Assert.That(session.NextFrame, Is.Zero);
        Assert.That(requests.Count, Is.Zero);

        session.ReceiveRemoteInput(0, PlayerInput.None);
        AdvanceResult advanced = session.AdvanceFrame(requests);
        var executor = new RollbackRequestExecutor(ArenaGame.Create(), 256);
        executor.Execute(requests);
        Assert.That(advanced.ForwardAdvanced, Is.True);
        Assert.That(advanced.StallReason, Is.EqualTo(StallReason.None));
        Assert.That(session.NextFrame, Is.EqualTo(1));
        Assert.That(executor.State.Frame, Is.EqualTo(session.NextFrame));
        Assert.That(session.Statistics.Stalls, Is.EqualTo(2), "Successful advance must not count as another stall.");
        Assert.That(session.Statistics.Rollbacks, Is.Zero);
        Assert.That(session.Statistics.ResimulatedFrames, Is.Zero);
        Assert.That(session.Statistics.MaxRollbackDistance, Is.Zero);
    }

    [Test]
    public void TooSmallBufferDoesNotAppendHalfABatchOrAdvance()
    {
        var session = new RollbackSession(new SessionOptions(0, 0), 0);
        session.CaptureLocalInput(0, PlayerInput.Right);
        session.ReceiveRemoteInput(0, PlayerInput.None);
        var requests = new RollbackRequestBuffer(1);

        var error = Assert.Throws<RollbackException>(() => session.AdvanceFrame(requests));
        Assert.That(error!.Error, Is.EqualTo(RollbackError.BufferTooSmall));
        Assert.That(requests.Count, Is.Zero, "Reject before appending Save when Step would not fit.");
        Assert.That(session.NextFrame, Is.Zero);
        Assert.That(session.Statistics.Stalls, Is.Zero, "Invalid request capacity is an error, not a stall.");
    }
}
