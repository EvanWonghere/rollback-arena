using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Gameplay;

public class GameplayEdges
{
    const long One = 1L << 32;
    static GameState Fight()
    {
        var s = ArenaGame.Create();
        s.Phase = MatchPhase.Fight;
        s.Player1.X = Fixed64.Zero;
        s.Player2.X = Fixed64.FromRaw(3L << 31);
        return s;
    }
    static void Step(ref GameState s, PlayerInput first = PlayerInput.None) => ArenaGame.Step(ref s, new FrameInputs(first, PlayerInput.None));

    [Test]
    public void OpposingDirectionsCancelWithoutChangingFacing()
    {
        var s = Fight();
        Step(ref s, PlayerInput.Left | PlayerInput.Right | PlayerInput.Up | PlayerInput.Down);
        Assert.That(s.Player1.X, Is.EqualTo(Fixed64.Zero));
        Assert.That(s.Player1.Z, Is.EqualTo(Fixed64.Zero));
        Assert.That(s.Player1.FacingX, Is.EqualTo(1));
    }

    [Test]
    public void DiagonalWalkingAndDashUseTheSpecifiedRawConstant()
    {
        var s = Fight();
        s.Player2.Z = Fixed64.FromInt(-5);
        Fixed64 move = GameplayRules.SqrtHalf * GameplayRules.MoveSpeed;
        for (int i = 0; i < 16; i++) Step(ref s, PlayerInput.Right | PlayerInput.Up);
        Assert.That(s.Player1.X, Is.EqualTo(move * Fixed64.FromInt(16)));
        Assert.That(s.Player1.Z, Is.EqualTo(move * Fixed64.FromInt(16)));
        s = Fight();
        s.Player2.Z = Fixed64.FromInt(-5);
        for (int i = 0; i < 8; i++) Step(ref s, i == 0 ? PlayerInput.Right | PlayerInput.Up | PlayerInput.Dash : PlayerInput.None);
        Fixed64 dash = GameplayRules.SqrtHalf * GameplayRules.DashSpeed * Fixed64.FromInt(8);
        Assert.That(s.Player1.X, Is.EqualTo(dash));
        Assert.That(s.Player1.Z, Is.EqualTo(dash));
    }

    [Test]
    public void DiagonalKnockbackUsesAttackerFacing()
    {
        var s = Fight();
        s.Player1.FacingZ = 1;
        s.Player1.Action = PlayerAction.Attack;
        s.Player1.ActionFrame = 6;
        s.Player2.X = Fixed64.One;
        s.Player2.Z = Fixed64.One;
        Step(ref s);
        Fixed64 speed = GameplayRules.SqrtHalf * Fixed64.FromRaw(One / 8);
        Assert.That(s.Player2.KnockbackX, Is.EqualTo(speed));
        Assert.That(s.Player2.KnockbackZ, Is.EqualTo(speed));
        for (int i = 0; i < 16; i++) Step(ref s);
        Assert.That(s.Player2.X, Is.EqualTo(Fixed64.One + speed * Fixed64.FromInt(16)));
        Assert.That(s.Player2.Z, Is.EqualTo(Fixed64.One + speed * Fixed64.FromInt(16)));
    }

    [TestCase(-(7L << 30), false)]
    [TestCase(-(7L << 30) + 1, true)]
    [TestCase(One - 1, true)]
    [TestCase(One, false)]
    public void VerticalHitBoundsAreStrict(long y, bool hits)
    {
        var s = Fight();
        s.Player1.Action = PlayerAction.Attack;
        s.Player1.ActionFrame = 6;
        s.Player2.Y = Fixed64.FromRaw(y);
        s.Player2.Grounded = false;
        Step(ref s);
        Assert.That(s.Player2.HitCount, Is.EqualTo(hits ? 1 : 0));
    }

    [TestCase(6, false)]
    [TestCase(7, true)]
    [TestCase(8, true)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    public void OnlyActionFramesSevenThroughNineAreActive(int actionFrame, bool hits)
    {
        var s = Fight();
        s.Player1.Action = PlayerAction.Attack;
        s.Player1.ActionFrame = actionFrame - 1;
        Step(ref s);
        Assert.That(s.Player2.HitCount, Is.EqualTo(hits ? 1 : 0));
    }

    [Test]
    public void HitstunCanBeResetByAnotherHit()
    {
        var s = Fight();
        s.Player1.Action = PlayerAction.Attack;
        s.Player1.ActionFrame = 6;
        s.Player2.Action = PlayerAction.Hitstun;
        s.Player2.ActionFrame = 10;
        s.Player2.HitCount = 2;
        Step(ref s);
        Assert.That(s.Player2.ActionFrame, Is.Zero);
        Assert.That(s.Player2.HitCount, Is.EqualTo(3));
        Assert.That(s.Player2.KnockbackX, Is.EqualTo(Fixed64.FromRaw(One / 4)));
    }

    [Test]
    public void FallingIgnoresNewActionsAndKeepsHorizontalVelocity()
    {
        var s = Fight();
        s.Player1.X = Fixed64.FromInt(7);
        s.Player1.Y = -Fixed64.One;
        s.Player1.Grounded = false;
        s.Player1.Vx = Fixed64.FromRaw(One / 8);
        Step(ref s, PlayerInput.Left | PlayerInput.Attack | PlayerInput.Dash | PlayerInput.Jump);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Idle));
        Assert.That(s.Player1.FacingX, Is.EqualTo(1));
        Assert.That(s.Player1.X, Is.EqualTo(Fixed64.FromInt(7) + Fixed64.FromRaw(One / 8)));
    }

    [Test]
    public void AttackLocksFacingAndDashBeatsJump()
    {
        var s = Fight();
        Step(ref s, PlayerInput.Attack);
        Step(ref s, PlayerInput.Left);
        Assert.That(s.Player1.FacingX, Is.EqualTo(1));
        s = Fight();
        Step(ref s, PlayerInput.Dash | PlayerInput.Jump);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Dash));
        Assert.That(s.Player1.Grounded, Is.True);
        s = Fight();
        s.Player1.Grounded = false;
        s.Player1.Y = Fixed64.One;
        Step(ref s, PlayerInput.Dash | PlayerInput.Jump);
        Assert.That(s.Player1.Action, Is.EqualTo(PlayerAction.Idle));
    }

    [Test]
    public void TimeoutHitCountPrecedesDistance()
    {
        var s = Fight();
        s.PhaseFrame = 3599;
        s.Player1.X = Fixed64.FromInt(-5);
        s.Player2.X = Fixed64.Zero;
        s.Player2.HitCount = 1;
        Step(ref s);
        Assert.That(s.LastRoundResult, Is.EqualTo(RoundResult.Player1));
    }

    [Test]
    public void OutroDisablesHitsAndRoundResetClearsEveryPlayerField()
    {
        var s = Fight();
        s.Phase = MatchPhase.Outro;
        s.LastRoundResult = RoundResult.Player1;
        s.Player1Wins = 1;
        s.Player1.Action = PlayerAction.Attack;
        s.Player1.ActionFrame = 6;
        Step(ref s);
        Assert.That(s.Player2.HitCount, Is.Zero);
        Assert.That(s.Player1Wins, Is.EqualTo(1));
        s.Player1.HitCount = 99;
        s.Player2.PreviousInput = PlayerInput.Dash;
        s.Player2.DashCooldown = 19;
        s.PhaseFrame = 119;
        Step(ref s);
        var initial = ArenaGame.Create();
        Assert.That(s.Player1, Is.EqualTo(initial.Player1));
        Assert.That(s.Player2, Is.EqualTo(initial.Player2));
        Assert.That(s.RoundNumber, Is.EqualTo(2));
        Assert.That(s.LastRoundResult, Is.EqualTo(RoundResult.Player1));
    }

    [Test]
    public void TinyNonzeroSeparationDoesNotUseCoincidentXAxisFallback()
    {
        var s = Fight();
        s.Player2.X = Fixed64.Zero;
        s.Player2.Z = Fixed64.Epsilon;
        Step(ref s);
        Assert.That(s.Player1.X, Is.EqualTo(Fixed64.Zero));
        Assert.That(s.Player2.X, Is.EqualTo(Fixed64.Zero));
        Assert.That(s.Player1.Z < Fixed64.Zero, Is.True);
        Assert.That(s.Player2.Z > Fixed64.Zero, Is.True);
    }

    [Test]
    public void ReservedInputBitIsRejected()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new FrameInputs((PlayerInput)128, PlayerInput.None));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new FrameInputs(PlayerInput.None, (PlayerInput)128));
    }
}
