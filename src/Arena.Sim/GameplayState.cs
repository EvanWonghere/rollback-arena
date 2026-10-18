namespace Arena.Sim
{
    public enum MatchPhase : byte { Countdown, Fight, Outro, MatchOver }
    public enum PlayerAction : byte { Idle, Attack, Dash, Hitstun }
    public enum RoundResult : byte { None, Player1, Player2, Draw }

    [System.Flags]
    public enum PlayerInput : byte
    {
        None = 0, Up = 1, Down = 2, Left = 4, Right = 8,
        Attack = 16, Jump = 32, Dash = 64
    }

    public readonly struct FrameInputs
    {
        public readonly PlayerInput Player1;
        public readonly PlayerInput Player2;
        public FrameInputs(PlayerInput player1, PlayerInput player2)
        {
            if ((((byte)player1 | (byte)player2) & 128) != 0)
                throw new System.ArgumentOutOfRangeException(nameof(player1), "Reserved input bit must be zero.");
            Player1 = player1;
            Player2 = player2;
        }
    }

    public struct PlayerState
    {
        public Fixed64 X, Y, Z;
        public Fixed64 Vx, Vy, Vz;
        public Fixed64 KnockbackX, KnockbackZ;
        public sbyte FacingX, FacingZ, DashX, DashZ;
        public bool Grounded, HitThisAttack;
        public PlayerAction Action;
        public int ActionFrame, DashCooldown, HitCount;
        public PlayerInput PreviousInput;
        public bool Falling => !Grounded && Y < Fixed64.Zero;
    }

    public struct GameState
    {
        public long Frame;
        public MatchPhase Phase;
        public int PhaseFrame, RoundNumber;
        public byte Player1Wins, Player2Wins;
        public RoundResult LastRoundResult;
        public PlayerState Player1, Player2;
    }

    /// <summary>状态全部由值组成，不含数组或对象引用；捕获和恢复均是完整值复制。</summary>
    public readonly struct GameSnapshot
    {
        readonly GameState saved;
        public GameSnapshot(in GameState state) { saved = state; }
        public void Restore(ref GameState state) { state = saved; }
    }
}
