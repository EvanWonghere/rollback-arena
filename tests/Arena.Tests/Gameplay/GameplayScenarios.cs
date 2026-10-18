using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Gameplay;

public class GameplayScenarios
{
    static Fixed64 Q(int numerator, int denominator = 1) => Fixed64.FromRaw(((long)numerator << 32) / denominator);
    static GameState Fight(Fixed64? secondX = null)
    {
        var state = ArenaGame.Create();
        state.Frame = 0;
        state.Phase = MatchPhase.Fight;
        state.Player1.X = Fixed64.Zero;
        state.Player2.X = secondX ?? Fixed64.Zero;
        state.Player2.Z = secondX.HasValue ? Fixed64.Zero : Q(-5);
        return state;
    }
    static void Step(ref GameState state, PlayerInput first = PlayerInput.None, PlayerInput second = PlayerInput.None) =>
        ArenaGame.Step(ref state, new FrameInputs(first, second));
    static void Through(ref GameState state, int lastFrame, System.Func<long, PlayerInput>? first = null, System.Func<long, PlayerInput>? second = null)
    {
        while (state.Frame <= lastFrame) Step(ref state, first?.Invoke(state.Frame) ?? PlayerInput.None, second?.Invoke(state.Frame) ?? PlayerInput.None);
    }

    [Test]
    public void S01WalkStopsOnRelease()
    {
        var s = Fight();
        Through(ref s, 31, _ => PlayerInput.Right);
        Assert.That(s.Player1.X, Is.EqualTo(Q(3)));
        Through(ref s, 40);
        Assert.That(s.Player1.X, Is.EqualTo(Q(3)));
    }

    [TestCase(22, false)]
    [TestCase(23, true)]
    public void S02AttackTimingRecoveryAndNoBuffer(int press, bool starts)
    {
        var s = Fight(Q(3, 2));
        Step(ref s, PlayerInput.Attack);
        Through(ref s, 5);
        Assert.That(s.Player2.HitCount, Is.Zero);
        Step(ref s);
        Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        Through(ref s, 21);
        Assert.That(s.Player2.X, Is.EqualTo(Q(27, 8)));
        Step(ref s, press == 22 ? PlayerInput.Attack : PlayerInput.None);
        Assert.That(s.Player2.X, Is.EqualTo(Q(7, 2)));
        Step(ref s, PlayerInput.Attack, PlayerInput.Attack);
        Assert.That(s.Player1.Action, Is.EqualTo(starts ? PlayerAction.Attack : PlayerAction.Idle));
        Assert.That(s.Player2.Action, Is.EqualTo(PlayerAction.Attack));
        if (starts) Assert.That(s.Player1.ActionFrame, Is.EqualTo(1));
    }

    [Test]
    public void S03GrowingKnockbackLeavesPlatformAndLosesRound()
    {
        var s = Fight(Q(3, 2));
        s.Player2.HitCount = 3;
        Step(ref s, PlayerInput.Attack);
        Through(ref s, 6);
        Assert.That(s.Player2.KnockbackX, Is.EqualTo(Q(5, 16)));
        Through(ref s, 20);
        Assert.That(s.Player2.X, Is.EqualTo(Q(47, 8)));
        Assert.That(s.Player2.Grounded, Is.True);
        Step(ref s);
        Assert.That(s.Player2.Grounded, Is.False);
        Through(ref s, 52);
        Assert.That(s.Player2.Y, Is.EqualTo(Q(-31, 8)));
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Fight));
        Step(ref s);
        Assert.Multiple(() =>
        {
            Assert.That(s.Player2.HitCount, Is.EqualTo(4));
            Assert.That(s.LastRoundResult, Is.EqualTo(RoundResult.Player1));
            Assert.That(s.Player1Wins, Is.EqualTo(1));
        });
    }

    [Test]
    public void S04JumpApexAndLandingFrames()
    {
        var s = Fight();
        Step(ref s, PlayerInput.Jump);
        Through(ref s, 15);
        Assert.That(s.Player1.Y, Is.EqualTo(Q(17, 16)));
        Step(ref s);
        Assert.That(s.Player1.Y, Is.EqualTo(Q(17, 16)));
        Through(ref s, 31);
        Assert.That(s.Player1.Y, Is.EqualTo(Q(1, 8)));
        Step(ref s);
        Assert.That(s.Player1.Y, Is.EqualTo(Fixed64.Zero));
        Assert.That(s.Player1.Grounded, Is.True);
    }

    [TestCase(32, false)]
    [TestCase(33, true)]
    public void S04JumpCannotStartBeforeLandingStepCompletes(int press, bool jumps)
    {
        var s = Fight();
        Step(ref s, PlayerInput.Jump);
        Through(ref s, press - 1);
        Step(ref s, PlayerInput.Jump);
        Assert.That(s.Player1.Grounded, Is.EqualTo(!jumps));
        Assert.That(s.Player1.Y, Is.EqualTo(jumps ? Q(1, 8) : Q(0)));
    }

    [TestCase(6, -1)]
    [TestCase(10, -1)]
    [TestCase(5, 11)]
    [TestCase(14, 20)]
    public void S05JumpDodgesOnlyDuringSpecifiedVerticalWindow(int attackFrame, int hitFrame)
    {
        var s = Fight(Q(3, 2));
        Step(ref s, PlayerInput.None, PlayerInput.Jump);
        Through(ref s, attackFrame - 1);
        Step(ref s, PlayerInput.Attack);
        int lastActive = attackFrame + 8;
        if (hitFrame >= 0)
        {
            Through(ref s, hitFrame - 1);
            Assert.That(s.Player2.HitCount, Is.Zero);
            Step(ref s);
            Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        }
        else
        {
            Through(ref s, lastActive);
            Assert.That(s.Player2.HitCount, Is.Zero);
        }
        if (attackFrame == 14)
        {
            Through(ref s, 32);
            Assert.That(s.Player2.Grounded, Is.True);
            Assert.That(s.Player2.X, Is.EqualTo(Q(3)));
            Through(ref s, 36);
            Assert.That(s.Player2.X, Is.EqualTo(Q(7, 2)));
        }
    }

    [Test]
    public void S06SimultaneousHitsAndPlayerSwapSymmetry()
    {
        var s = Fight(Q(3, 2));
        var swapped = s;
        (swapped.Player1, swapped.Player2) = (s.Player2, s.Player1);
        Step(ref s, PlayerInput.Attack, PlayerInput.Attack);
        Step(ref swapped, PlayerInput.Attack, PlayerInput.Attack);
        Through(ref s, 6);
        Through(ref swapped, 6);
        Assert.That(s.Player1.HitCount, Is.EqualTo(1));
        Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        Through(ref s, 22);
        Through(ref swapped, 22);
        Assert.That(s.Player1.X, Is.EqualTo(Q(-2)));
        Assert.That(s.Player2.X, Is.EqualTo(Q(7, 2)));
        (swapped.Player1, swapped.Player2) = (swapped.Player2, swapped.Player1);
        Assert.That(StateChecksum.Compute(swapped), Is.EqualTo(StateChecksum.Compute(s)));
    }

    [TestCase(29, false)]
    [TestCase(30, true)]
    public void S07DashDurationAndCooldown(int secondDash, bool starts)
    {
        var s = Fight();
        Step(ref s, PlayerInput.Dash);
        Through(ref s, 6);
        Assert.That(s.Player1.X, Is.EqualTo(Q(21, 8)));
        Step(ref s);
        Assert.That(s.Player1.X, Is.EqualTo(Q(3)));
        Step(ref s);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Idle));
        Through(ref s, secondDash - 1);
        Step(ref s, PlayerInput.Dash);
        Assert.That(s.Player1.Action, Is.EqualTo(starts ? PlayerAction.Dash : PlayerAction.Idle));
    }

    [Test]
    public void S07PlatformEqualityAndDashFacing()
    {
        var s = Fight();
        Through(ref s, 38, f => f == 0 || f == 31 ? PlayerInput.Dash : PlayerInput.None);
        Assert.That(s.Player1.X, Is.EqualTo(Q(6)));
        Assert.That(s.Player1.Grounded, Is.True);
        Step(ref s, PlayerInput.Right);
        Assert.That(s.Player1.Grounded, Is.False);
        var other = Fight();
        Step(ref other, PlayerInput.Up | PlayerInput.Dash);
        Assert.That(other.Player1.FacingX, Is.Zero);
        Assert.That(other.Player1.FacingZ, Is.EqualTo(1));
        Assert.That(other.Player1.Z, Is.EqualTo(Q(3, 8)));
    }

    [Test]
    public void S08WalkOffFallsOnExactFrames()
    {
        var s = Fight(Q(-3));
        s.Player1.X = Q(23, 4);
        Through(ref s, 1, _ => PlayerInput.Right);
        Assert.That(s.Player1.Grounded, Is.True);
        Step(ref s, PlayerInput.Right);
        Assert.That(s.Player1.Grounded, Is.False);
        Through(ref s, 33, _ => PlayerInput.Right);
        Assert.That(s.Player1.Y, Is.EqualTo(Q(-31, 8)));
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Fight));
        Step(ref s, PlayerInput.Right);
        Assert.That(s.LastRoundResult, Is.EqualTo(RoundResult.Player2));
    }

    [TestCase(3, true)]
    [TestCase(4, false)]
    public void S08OneFrameWalkBackGrace(int turnFrame, bool lands)
    {
        var s = Fight(Q(-3));
        s.Player1.X = Q(23, 4);
        Through(ref s, turnFrame - 1, _ => PlayerInput.Right);
        Step(ref s, PlayerInput.Left);
        Assert.That(s.Player1.Grounded, Is.EqualTo(lands));
        if (lands) Assert.That(s.Player1.X, Is.EqualTo(Q(95, 16)));
        else Assert.That(s.Player1.Y < Fixed64.Zero, Is.True);
    }

    [Test]
    public void S09SimultaneousFallIsDraw()
    {
        var s = Fight(Q(23, 4));
        s.Player1.X = Q(-23, 4);
        Through(ref s, 34, _ => PlayerInput.Left, _ => PlayerInput.Right);
        Assert.That(s.LastRoundResult, Is.EqualTo(RoundResult.Draw));
        Assert.That(s.Player1Wins, Is.Zero);
        Assert.That(s.Player2Wins, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void S10BodyPushAndCoincidentFallback(bool coincident)
    {
        var s = Fight(coincident ? Q(0) : Q(1, 2));
        Step(ref s);
        Assert.That(s.Player1.X, Is.EqualTo(coincident ? Q(-1, 2) : Q(-1, 4)));
        Assert.That(s.Player2.X, Is.EqualTo(coincident ? Q(1, 2) : Q(3, 4)));
    }

    [Test]
    public void S11OneHitPerAttack()
    {
        var s = Fight(Q(1));
        Step(ref s, PlayerInput.Attack);
        Through(ref s, 6);
        Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        Through(ref s, 8);
        Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        Through(ref s, 22);
        Assert.That(s.Player2.X, Is.EqualTo(Q(3)));
        Step(ref s);
        Assert.That(s.Player2.Action, Is.EqualTo(PlayerAction.Idle));
    }

    [Test]
    public void S12DashInterruptedAtInclusiveHitBoundaryAndCooldownContinues()
    {
        var s = Fight(Q(7, 2));
        Through(ref s, 5, f => f == 0 ? PlayerInput.Attack : PlayerInput.None,
            f => f == 3 ? PlayerInput.Left | PlayerInput.Dash : PlayerInput.None);
        Assert.That(s.Player2.X, Is.EqualTo(Q(19, 8)));
        Step(ref s);
        Assert.That(s.Player2.X, Is.EqualTo(Q(2)));
        Assert.That(s.Player2.Action, Is.EqualTo(PlayerAction.Hitstun));
        Assert.That(s.Player2.HitCount, Is.EqualTo(1));
        Through(ref s, 22);
        Assert.That(s.Player2.X, Is.EqualTo(Q(4)));
        Through(ref s, 31);
        var allowed = s;
        Step(ref allowed);
        Step(ref allowed, PlayerInput.None, PlayerInput.Dash);
        Assert.That(allowed.Player2.Action, Is.EqualTo(PlayerAction.Dash), "dash can start exactly at frame 33");
        Step(ref s, PlayerInput.None, PlayerInput.Dash);
        Assert.That(s.Player2.Action, Is.EqualTo(PlayerAction.Idle));
        Step(ref s);
        Step(ref s, PlayerInput.None, PlayerInput.Dash);
        Assert.That(s.Player2.Action, Is.EqualTo(PlayerAction.Dash));
    }

    [TestCase(PlayerInput.Right)]
    [TestCase(PlayerInput.Attack)]
    public void S13CountdownSuppressesInputAndFirstFightFrameCreatesEdge(PlayerInput input)
    {
        var s = ArenaGame.Create();
        Through(ref s, 89, _ => input);
        Assert.That(s.Player1.X, Is.EqualTo(Q(-5, 2)));
        Assert.That(s.Player1.PreviousInput, Is.EqualTo(PlayerInput.None));
        Step(ref s, input);
        if (input == PlayerInput.Right) Assert.That(s.Player1.X, Is.EqualTo(Q(-77, 32)));
        else Assert.That(s.Player1.ActionFrame, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void S14TimeoutAndNextRound(bool movesCloser)
    {
        var s = ArenaGame.Create();
        Through(ref s, 3689, f => movesCloser && f >= 90 && f <= 97 ? PlayerInput.Right : PlayerInput.None);
        Assert.That(s.LastRoundResult, Is.EqualTo(movesCloser ? RoundResult.Player1 : RoundResult.Draw));
        Assert.That(s.Player1Wins, Is.EqualTo(movesCloser ? 1 : 0));
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Outro));
        Through(ref s, 3808);
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Outro));
        Step(ref s);
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Countdown));
        Assert.That(s.RoundNumber, Is.EqualTo(2));
        Assert.That(s.Player1.X, Is.EqualTo(Q(-5, 2)));
        Step(ref s);
        Assert.That(s.PhaseFrame, Is.EqualTo(1));
    }

    [Test]
    public void S15FullMatchAndFrozenTerminalState()
    {
        var s = ArenaGame.Create();
        Through(ref s, 158, second: _ => PlayerInput.Right);
        Assert.That(s.Player1Wins, Is.Zero);
        Step(ref s, second: PlayerInput.Right);
        Assert.That(s.Player1Wins, Is.EqualTo(1));
        Through(ref s, 369, second: _ => PlayerInput.Right);
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.Fight));
        Assert.That(s.PhaseFrame, Is.Zero);
        Step(ref s, second: PlayerInput.Right);
        Assert.That(s.PhaseFrame, Is.EqualTo(1));
        Through(ref s, 439, second: _ => PlayerInput.Right);
        Assert.That(s.Player1Wins, Is.EqualTo(2));
        Through(ref s, 559, second: _ => PlayerInput.Right);
        Assert.That(s.Phase, Is.EqualTo(MatchPhase.MatchOver));
        var finished = s;
        Through(ref s, 659, _ => PlayerInput.Attack | PlayerInput.Jump, _ => PlayerInput.Dash);
        Assert.That(s.Frame, Is.EqualTo(finished.Frame + 100));
        s.Frame = finished.Frame;
        Assert.That(StateChecksum.Compute(s), Is.EqualTo(StateChecksum.Compute(finished)));
    }

    [Test]
    public void S16PriorityAndHeldButtonsDoNotRetrigger()
    {
        var s = Fight();
        Step(ref s, PlayerInput.Attack | PlayerInput.Jump);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Attack));
        Assert.That(s.Player1.Grounded, Is.True);
        Through(ref s, 59, _ => PlayerInput.Attack);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Idle));
        Assert.That(s.Player1.Grounded, Is.True);
    }
}
