using System;
using Arena.Net;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Network;

public class ReliableInputChannelTests
{
    [Test]
    public void BothDirectionsDeliverAndAckAllInputsDespiteLossDuplicationAndJitter()
    {
        var conditions = new NetworkConditions(3, 4, 100, 200, 0, 5);
        var network = new SimulatedNetwork(7, conditions, conditions);
        var options = new SessionOptions();
        var first = new ReliableInputChannel(options, 0, network.First);
        var second = new ReliableInputChannel(options, 1, network.Second);
        for (int frame = 2; frame < 46; frame++)
        {
            first.QueueLocalInput(new FramedInput(frame, (PlayerInput)(frame & 127)));
            second.QueueLocalInput(new FramedInput(frame, (PlayerInput)((frame + 16) & 127)));
        }
        var seenFirst = new bool[46];
        var seenSecond = new bool[46];
        long ackFirst = 1, ackSecond = 1;
        // 传输测试自己的收据 oracle，不在生产通道中计算会话确认。
        for (int tick = 0; tick < 2000; tick++)
        {
            network.AdvanceTo(tick);
            first.ReceiveAvailable(input =>
            {
                Assert.That(input.Value, Is.EqualTo((PlayerInput)((input.Frame + 16) & 127)));
                seenFirst[input.Frame] = true;
                while (ackFirst + 1 < 46 && seenFirst[ackFirst + 1]) ackFirst++;
            });
            second.ReceiveAvailable(input =>
            {
                Assert.That(input.Value, Is.EqualTo((PlayerInput)(input.Frame & 127)));
                seenSecond[input.Frame] = true;
                while (ackSecond + 1 < 46 && seenSecond[ackSecond + 1]) ackSecond++;
            });
            first.SendPending(ackFirst);
            second.SendPending(ackSecond);
            if (first.PendingCount == 0 && second.PendingCount == 0)
            {
                Assert.That(ackFirst, Is.EqualTo(45));
                Assert.That(ackSecond, Is.EqualTo(45));
                Assert.That(network.Statistics.Dropped, Is.GreaterThan(0));
                TestContext.Out.WriteLine($"Reliable channel: drained at tick {tick}, dropped={network.Statistics.Dropped}, duplicates={network.Statistics.Duplicated}");
                return;
            }
        }
        Assert.Fail("Reliable transfer failed to drain within explicit tick budget.");
    }

    [Test]
    public void LastPacketLossRecoversWithoutAnyNewCapturedInput()
    {
        var network = new SimulatedNetwork(1, new NetworkConditions(burstLength: 1), new NetworkConditions(burstLength: 1));
        var options = new SessionOptions(0);
        var first = new ReliableInputChannel(options, 0, network.First);
        var second = new ReliableInputChannel(options, 1, network.Second);
        first.QueueLocalInput(new FramedInput(0, PlayerInput.Attack));
        second.QueueLocalInput(new FramedInput(0, PlayerInput.Jump));
        long firstAck = -1, secondAck = -1;
        for (int tick = 0; tick < 20; tick++)
        {
            network.AdvanceTo(tick);
            first.ReceiveAvailable(input => { Assert.That(input.Value, Is.EqualTo(PlayerInput.Jump)); firstAck = 0; });
            second.ReceiveAvailable(input => { Assert.That(input.Value, Is.EqualTo(PlayerInput.Attack)); secondAck = 0; });
            first.SendPending(firstAck);
            second.SendPending(secondAck);
            if (first.PendingCount == 0 && second.PendingCount == 0)
            {
                Assert.That(tick, Is.GreaterThan(1));
                Assert.That(network.Statistics.Dropped, Is.EqualTo(2));
                return;
            }
        }
        Assert.Fail("Tail retransmission / ACK-only packets did not finish.");
    }

    [Test]
    public void ConflictsGapsAndLiveHistoryOverwriteAreRejected()
    {
        var network = new SimulatedNetwork(1, default, default);
        var channel = new ReliableInputChannel(new SessionOptions(0, 0, 2, 3), 0, network.First);
        Assert.That(Assert.Throws<RollbackException>(() => channel.QueueLocalInput(new FramedInput(1, PlayerInput.None)))!.Error, Is.EqualTo(RollbackError.FrameMismatch));
        channel.QueueLocalInput(new FramedInput(0, PlayerInput.Attack));
        channel.QueueLocalInput(new FramedInput(0, PlayerInput.Attack));
        Assert.That(channel.PendingCount, Is.EqualTo(1));
        Assert.That(Assert.Throws<RollbackException>(() => channel.QueueLocalInput(new FramedInput(0, PlayerInput.Left)))!.Error, Is.EqualTo(RollbackError.ConflictingInput));
        channel.QueueLocalInput(new FramedInput(1, PlayerInput.None));
        Assert.That(Assert.Throws<RollbackException>(() => channel.QueueLocalInput(new FramedInput(2, PlayerInput.None)))!.Error, Is.EqualTo(RollbackError.HistoryOverflow));
        Assert.That(channel.PendingCount, Is.EqualTo(2));
        var delayed = new ReliableInputChannel(new SessionOptions(), 0, network.First);
        Assert.That(Assert.Throws<RollbackException>(() => delayed.QueueLocalInput(new FramedInput(0, PlayerInput.Right)))!.Error, Is.EqualTo(RollbackError.ConflictingInput));
    }

    static void SendAck(ITransport sender, long ack)
    {
        var bytes = new byte[27];
        InputPacketCodec.Encode(new InputPacketHeader(1, 1, 0, 0, ack), Array.Empty<PlayerInput>(), bytes);
        sender.Send(bytes);
    }

    [Test]
    public void FutureAckIsRejectedAndOlderAckDoesNotRegress()
    {
        var network = new SimulatedNetwork(1, default, default);
        var channel = new ReliableInputChannel(new SessionOptions(0), 0, network.First);
        SendAck(network.Second, 0);
        Assert.That(Assert.Throws<RollbackException>(() => channel.ReceiveAvailable(_ => Assert.Fail("ACK must not deliver input")))!.Error, Is.EqualTo(RollbackError.InvalidAck));
        channel.QueueLocalInput(new FramedInput(0, PlayerInput.Right));
        channel.QueueLocalInput(new FramedInput(1, PlayerInput.Left));
        channel.SendPending(-1);
        SendAck(network.Second, 1);
        channel.ReceiveAvailable(_ => Assert.Fail("ACK-only has no input"));
        Assert.That(channel.PendingCount, Is.Zero);
        SendAck(network.Second, 0);
        channel.ReceiveAvailable(_ => Assert.Fail("ACK-only has no input"));
        Assert.That(channel.AcknowledgedThrough, Is.EqualTo(1));
        Assert.Throws<RollbackException>(() => channel.SendPending(-2));
    }

    [Test]
    public void CorruptPayloadIsNotPartiallyDelivered()
    {
        var network = new SimulatedNetwork(1, default, default);
        var channel = new ReliableInputChannel(new SessionOptions(0), 0, network.First);
        var bytes = new byte[29];
        InputPacketCodec.Encode(new InputPacketHeader(1, 1, 0, 2, -1), new[] { PlayerInput.Right, PlayerInput.Attack }, bytes);
        bytes[28] = 128;
        network.Second.Send(bytes);
        int delivered = 0;
        Assert.Throws<RollbackException>(() => channel.ReceiveAvailable(_ => delivered++));
        Assert.That(delivered, Is.Zero);
    }
}
