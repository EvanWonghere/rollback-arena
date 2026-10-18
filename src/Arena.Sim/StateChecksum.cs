namespace Arena.Sim
{
    /// <summary>FNV-1a 64，按显式字段的 little-endian 字节计算，不读取 padding。</summary>
    public static class StateChecksum
    {
        public static ulong Compute(in GameState state)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, 1, 1); // 格式版本
            Add(ref hash, unchecked((ulong)state.Frame), 8);
            Add(ref hash, (byte)state.Phase, 1);
            Add(ref hash, unchecked((uint)state.PhaseFrame), 4);
            Add(ref hash, unchecked((uint)state.RoundNumber), 4);
            Add(ref hash, state.Player1Wins, 1);
            Add(ref hash, state.Player2Wins, 1);
            Add(ref hash, (byte)state.LastRoundResult, 1);
            Player(ref hash, state.Player1);
            Player(ref hash, state.Player2);
            return hash;
        }

        static void Player(ref ulong hash, in PlayerState p)
        {
            Add(ref hash, unchecked((ulong)p.X.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.Y.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.Z.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.Vx.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.Vy.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.Vz.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.KnockbackX.RawValue), 8);
            Add(ref hash, unchecked((ulong)p.KnockbackZ.RawValue), 8);
            Add(ref hash, unchecked((byte)p.FacingX), 1);
            Add(ref hash, unchecked((byte)p.FacingZ), 1);
            Add(ref hash, unchecked((byte)p.DashX), 1);
            Add(ref hash, unchecked((byte)p.DashZ), 1);
            Add(ref hash, p.Grounded ? 1UL : 0, 1);
            Add(ref hash, p.HitThisAttack ? 1UL : 0, 1);
            Add(ref hash, (byte)p.Action, 1);
            Add(ref hash, unchecked((uint)p.ActionFrame), 4);
            Add(ref hash, unchecked((uint)p.DashCooldown), 4);
            Add(ref hash, unchecked((uint)p.HitCount), 4);
            Add(ref hash, (byte)p.PreviousInput, 1);
        }

        static void Add(ref ulong hash, ulong value, int bytes)
        {
            for (int i = 0; i < bytes; i++)
            {
                hash = unchecked((hash ^ (byte)value) * 1099511628211UL);
                value >>= 8;
            }
        }
    }
}
