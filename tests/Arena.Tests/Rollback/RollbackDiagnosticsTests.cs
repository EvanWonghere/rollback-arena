using System;
using Arena.Headless;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

public class RollbackDiagnosticsTests
{
    [TestCase("capture/send", 1UL, RollbackError.FrameMismatch, 3)]
    [TestCase("receive", 0UL, RollbackError.InvalidPacket, 0)]
    [TestCase("advance", 2UL, RollbackError.MissingHistory, 7)]
    public void FailureIncludesReproductionContextAndOriginalError(string phase, ulong seed, RollbackError error, long frame)
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            RollbackEquivalenceRunner.Run(seed, 1000, (options, slot) => new FaultSession(options, slot, phase)));

        Assert.That(failure!.Message, Does.Contain($"seed={seed}"));
        Assert.That(failure.Message, Does.Contain("tick="));
        Assert.That(failure.Message, Does.Contain("peer="));
        Assert.That(failure.Message, Does.Contain($"phase={phase}"));
        Assert.That(failure.Message, Does.Contain(error.ToString()));
        Assert.That(failure.Message, Does.Contain($"inputFrame={frame}"));
        Assert.That(failure.InnerException, Is.TypeOf<RollbackException>());
    }

    /// <summary>
    /// 仅用于在指定入口制造故障，不生成保存/推进请求，也不可能完成等价性运行。
    /// Receive 故障场景持续停等以让模拟网络交付包；不存在生产回退调度。
    /// </summary>
    sealed class FaultSession : IRollbackSession
    {
        readonly string phase;
        long stalls;
        public SessionOptions Options { get; }
        public int LocalSlot { get; }
        public long NextFrame => 0;
        public long LastConfirmedInputFrame => Options.InputDelayFrames - 1L;
        public long RemoteContiguousInputFrame => Options.InputDelayFrames - 1L;
        public RollbackStatistics Statistics => new RollbackStatistics(0, 0, stalls, 0);

        public FaultSession(SessionOptions options, int slot, string phase)
        { Options = options; LocalSlot = slot; this.phase = phase; }

        public FramedInput CaptureLocalInput(long frame, PlayerInput input) =>
            new FramedInput(frame + Options.InputDelayFrames + (phase == "capture/send" ? 1 : 0), input);

        public InputReceiveResult ReceiveRemoteInput(long frame, PlayerInput input)
        {
            if (phase == "receive") throw new RollbackException(RollbackError.InvalidPacket, "Diagnostic probe.", frame);
            return InputReceiveResult.Stored;
        }

        public AdvanceResult AdvanceFrame(RollbackRequestBuffer requests)
        {
            if (phase == "advance") throw new RollbackException(RollbackError.MissingHistory, "Diagnostic probe.", 7);
            stalls++;
            return new AdvanceResult(false, StallReason.NoLocalInput);
        }
    }
}
