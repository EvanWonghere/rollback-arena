namespace Arena.Sim
{
    public static class GameplayRules
    {
        public const int CountdownFrames = 90;
        public const int FightFrames = 3600;
        public const int OutroFrames = 120;
        public const int WinsToMatch = 2;
        public const int AttackStartup = 6, AttackActive = 3, AttackRecovery = 14;
        public const int AttackFrames = AttackStartup + AttackActive + AttackRecovery;
        public const int DashFrames = 8, DashCooldown = 30, HitstunFrames = 16;

        public static readonly Fixed64 PlatformRadiusSquared = Fixed64.FromInt(36);
        public static readonly Fixed64 SpawnX = Fixed64.FromRaw(5L << 31);
        public static readonly Fixed64 MoveSpeed = Fixed64.FromRaw(3L << 27);
        public static readonly Fixed64 JumpVelocity = Fixed64.FromRaw(1L << 29);
        public static readonly Fixed64 Gravity = Fixed64.FromRaw(1L << 25);
        public static readonly Fixed64 DashSpeed = Fixed64.FromRaw(3L << 29);
        public static readonly Fixed64 OutY = Fixed64.FromInt(-4);
        public static readonly Fixed64 HitboxOffset = Fixed64.FromRaw(3L << 30);
        public static readonly Fixed64 HitDistanceSquared = Fixed64.FromRaw(25L << 28);
        public static readonly Fixed64 HitVerticalLower = Fixed64.FromRaw(-(7L << 30));
        public static readonly Fixed64 HitVerticalUpper = Fixed64.One;
        public static readonly Fixed64 SqrtHalf = Fixed64.FromRaw(3037000500L);

        public static void Direction(sbyte x, sbyte z, out Fixed64 dx, out Fixed64 dz)
        {
            Fixed64 scale = x != 0 && z != 0 ? SqrtHalf : Fixed64.One;
            dx = Fixed64.FromInt(x) * scale;
            dz = Fixed64.FromInt(z) * scale;
        }
    }
}
