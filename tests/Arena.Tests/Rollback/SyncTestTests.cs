using System;
using Arena.Headless;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Rollback;

public class SyncTestTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    public void V4FullMatchEveryFrameRewindsAndMatchesOriginal(int distance)
    {
        var run = HeadlessMatch.Bots();
        Assert.That(SyncTestSession.Run(run.Inputs, distance, ArenaGame.Create()), Is.EqualTo(StateChecksum.Compute(run.FinalState)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    public void V4HeldAttackAndTerminalFramesPreserveInputEdges(int distance)
    {
        var state = ArenaGame.Create();
        state.Phase = MatchPhase.Fight;
        state.Player1.X = Fixed64.Zero;
        state.Player2.X = Fixed64.FromRaw(3L << 31);
        var inputs = new FrameInputs[1100];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new FrameInputs(PlayerInput.Attack, PlayerInput.Right);
        var expected = state;
        foreach (FrameInputs input in inputs) ArenaGame.Step(ref expected, input);
        Assert.That(expected.Phase, Is.EqualTo(MatchPhase.MatchOver));
        Assert.That(SyncTestSession.Run(inputs, distance, state), Is.EqualTo(StateChecksum.Compute(expected)));
    }

    [Test]
    public void V4ShortStreamAndInvalidArgumentsAreExplicit()
    {
        var inputs = new FrameInputs[] { default };
        var state = ArenaGame.Create();
        ArenaGame.Step(ref state, default);
        Assert.That(SyncTestSession.Run(inputs, 8, ArenaGame.Create()), Is.EqualTo(StateChecksum.Compute(state)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SyncTestSession.Run(inputs, 0, ArenaGame.Create()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SyncTestSession.Run(Array.Empty<FrameInputs>(), 8, ArenaGame.Create()));
    }
}
