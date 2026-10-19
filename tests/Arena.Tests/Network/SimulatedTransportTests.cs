using System;
using System.Collections.Generic;
using System.Linq;
using Arena.Net;
using Arena.Rollback;
using NUnit.Framework;

namespace Arena.Tests.Network;

public class SimulatedTransportTests
{
    [Test]
    public void SendAndReceiveNeverBorrowMutableBuffers()
    {
        var network = new SimulatedNetwork(1, new NetworkConditions(2, duplicatePermille: 1000), default);
        byte[] source = { 1, 2, 3 };
        network.First.Send(source);
        source[0] = 99;
        var output = new byte[59];
        Assert.That(network.Second.TryReceive(output, out _), Is.False);
        network.AdvanceTo(2);
        Assert.That(network.Second.TryReceive(output, out int length), Is.True);
        Assert.That(length, Is.EqualTo(3));
        Assert.That(output[0], Is.EqualTo(1));
        output[0] = 88;
        Assert.That(network.Second.TryReceive(output, out _), Is.True);
        Assert.That(output[0], Is.EqualTo(1));
        Assert.That(network.Statistics.Duplicated, Is.EqualTo(1));
    }

    [Test]
    public void EqualDueTicksUseSendOrderAndShortReceiveDoesNotConsume()
    {
        var network = new SimulatedNetwork(1, default, default);
        network.First.Send(new byte[] { 1, 2 });
        network.First.Send(new byte[] { 3 });
        Assert.Throws<ArgumentException>(() => network.Second.TryReceive(new byte[1], out _));
        var output = new byte[59];
        Assert.That(network.First.TryReceive(output, out _), Is.False);
        Assert.That(network.Second.TryReceive(output, out int length), Is.True);
        Assert.That(length, Is.EqualTo(2));
        Assert.That(output[0], Is.EqualTo(1));
        Assert.That(network.Second.TryReceive(output, out _), Is.True);
        Assert.That(output[0], Is.EqualTo(3));
        Assert.That(network.Second.TryReceive(output, out _), Is.False);
        network.AdvanceTo(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => network.AdvanceTo(9));
    }

    static (List<(int Tick, byte Value)> Delivered, NetworkStatistics Stats) Scenario(ulong seed)
    {
        var conditions = new NetworkConditions(2, 10, 100, 200);
        var network = new SimulatedNetwork(seed, conditions, conditions);
        var delivered = new List<(int, byte)>();
        var output = new byte[59];
        for (int tick = 0; tick < 140; tick++)
        {
            network.AdvanceTo(tick);
            if (tick < 120) network.First.Send(new[] { (byte)tick });
            while (network.Second.TryReceive(output, out _)) delivered.Add((tick, output[0]));
        }
        return (delivered, network.Statistics);
    }

    [Test]
    public void FixedSeedReproducesLossDuplicationAndReordering()
    {
        var first = Scenario(99);
        var second = Scenario(99);
        Assert.That(first.Delivered, Is.EqualTo(second.Delivered));
        Assert.That(first.Stats.Dropped, Is.EqualTo(second.Stats.Dropped));
        Assert.That(first.Stats.Dropped, Is.GreaterThan(0));
        Assert.That(first.Stats.Duplicated, Is.GreaterThan(0));
        Assert.That(first.Delivered.Count, Is.GreaterThan(0));
        Assert.That(first.Delivered.Zip(first.Delivered.Skip(1), (a, b) => b.Value < a.Value).Any(x => x), Is.True, "jitter must actually reorder some packets");
    }

    [Test]
    public void BurstLossAndQueueOverflowAreDifferentOutcomes()
    {
        var network = new SimulatedNetwork(1, new NetworkConditions(burstLength: 3), default);
        for (int tick = 0; tick < 4; tick++) { network.AdvanceTo(tick); network.First.Send(new byte[] { 5 }); }
        Assert.That(network.Statistics.Dropped, Is.EqualTo(3));
        Assert.That(network.Second.TryReceive(new byte[59], out _), Is.True);
        var full = new SimulatedNetwork(1, new NetworkConditions(duplicatePermille: 1000), default, 1);
        Assert.That(Assert.Throws<RollbackException>(() => full.First.Send(new byte[] { 1 }))!.Error, Is.EqualTo(RollbackError.QueueFull));
        Assert.That(full.Second.TryReceive(new byte[59], out _), Is.False, "no partial duplicate enqueue");
        Assert.That(full.Statistics.Dropped, Is.Zero);
        var lost = new SimulatedNetwork(1, new NetworkConditions(lossPermille: 1000), default);
        lost.First.Send(new byte[] { 1 });
        Assert.That(lost.Second.TryReceive(new byte[59], out _), Is.False);
        Assert.That(lost.Statistics.Dropped, Is.EqualTo(1));
    }

    [Test]
    public void NetAssemblyHasOnlyExpectedReferencesAndInvalidConfigIsRejected()
    {
        var names = typeof(SimulatedNetwork).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.That(names, Is.SubsetOf(new[] { "netstandard", "Arena.Sim", "Arena.Rollback" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkConditions(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkConditions(lossPermille: 1001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkConditions(burstStart: long.MaxValue, burstLength: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedNetwork(1, default, default, 0));
    }
}
