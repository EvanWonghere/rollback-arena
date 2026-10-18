namespace Arena.Sim
{
    public static class ArenaGame
    {
        public static GameState Create()
        {
            var state = new GameState { Phase = MatchPhase.Countdown, RoundNumber = 1 };
            ResetPlayers(ref state);
            return state;
        }

        static void ResetPlayers(ref GameState state)
        {
            state.Player1 = new PlayerState { X = -GameplayRules.SpawnX, FacingX = 1, Grounded = true };
            state.Player2 = new PlayerState { X = GameplayRules.SpawnX, FacingX = -1, Grounded = true };
        }

        public static void Step(ref GameState state, FrameInputs inputs)
        {
            if (state.Phase == MatchPhase.MatchOver) { state.Frame++; return; }
            PlayerState first = state.Player1, second = state.Player2;
            AdvancePlayer(ref first, state.Phase == MatchPhase.Fight ? inputs.Player1 : PlayerInput.None);
            AdvancePlayer(ref second, state.Phase == MatchPhase.Fight ? inputs.Player2 : PlayerInput.None);
            PushBodies(ref first, ref second);

            if (state.Phase == MatchPhase.Fight)
            {
                // 必须先判定双方，再修改任何动作；否则会吞掉同帧互中。
                bool firstHits = CanHit(first, second);
                bool secondHits = CanHit(second, first);
                if (firstHits)
                {
                    first.HitThisAttack = true;
                    ApplyHit(ref second, first.FacingX, first.FacingZ);
                }
                if (secondHits)
                {
                    second.HitThisAttack = true;
                    ApplyHit(ref first, second.FacingX, second.FacingZ);
                }
            }
            state.Player1 = first;
            state.Player2 = second;
            AdvanceRound(ref state);
            state.Frame++;
        }

        static void AdvancePlayer(ref PlayerState player, PlayerInput input)
        {
            PlayerInput pressed = input & ~player.PreviousInput;
            player.PreviousInput = input;
            if (player.DashCooldown > 0) player.DashCooldown--;
            if (player.Action != PlayerAction.Idle)
            {
                player.ActionFrame++;
                int duration = player.Action == PlayerAction.Attack ? GameplayRules.AttackFrames
                    : player.Action == PlayerAction.Dash ? GameplayRules.DashFrames : GameplayRules.HitstunFrames;
                if (player.ActionFrame > duration) { player.Action = PlayerAction.Idle; player.ActionFrame = 0; }
            }

            sbyte x = (sbyte)(((input & PlayerInput.Right) != 0 ? 1 : 0) - ((input & PlayerInput.Left) != 0 ? 1 : 0));
            sbyte z = (sbyte)(((input & PlayerInput.Up) != 0 ? 1 : 0) - ((input & PlayerInput.Down) != 0 ? 1 : 0));
            if (player.Action == PlayerAction.Idle && !player.Falling)
            {
                if (x != 0 || z != 0) { player.FacingX = x; player.FacingZ = z; }
                if ((pressed & PlayerInput.Attack) != 0)
                {
                    player.Action = PlayerAction.Attack;
                    player.ActionFrame = 1;
                    player.HitThisAttack = false;
                }
                else if ((pressed & PlayerInput.Dash) != 0 && player.Grounded && player.DashCooldown == 0)
                {
                    player.Action = PlayerAction.Dash;
                    player.ActionFrame = 1;
                    player.DashX = x != 0 || z != 0 ? x : player.FacingX;
                    player.DashZ = x != 0 || z != 0 ? z : player.FacingZ;
                    player.FacingX = player.DashX;
                    player.FacingZ = player.DashZ;
                    player.DashCooldown = GameplayRules.DashCooldown;
                }
                else if ((pressed & PlayerInput.Jump) != 0 && player.Grounded)
                {
                    player.Grounded = false;
                    player.Vy = GameplayRules.JumpVelocity;
                }
            }

            if (player.Action == PlayerAction.Hitstun)
            {
                player.Vx = player.KnockbackX;
                player.Vz = player.KnockbackZ;
            }
            else if (player.Action == PlayerAction.Dash)
            {
                GameplayRules.Direction(player.DashX, player.DashZ, out Fixed64 dx, out Fixed64 dz);
                player.Vx = dx * GameplayRules.DashSpeed;
                player.Vz = dz * GameplayRules.DashSpeed;
            }
            else if (player.Action == PlayerAction.Attack)
            {
                if (player.Grounded) { player.Vx = Fixed64.Zero; player.Vz = Fixed64.Zero; }
            }
            else if (!player.Falling)
            {
                GameplayRules.Direction(x, z, out Fixed64 dx, out Fixed64 dz);
                player.Vx = dx * GameplayRules.MoveSpeed;
                player.Vz = dz * GameplayRules.MoveSpeed;
            }

            player.X += player.Vx;
            player.Z += player.Vz;
            bool inside = DistanceSquared(player.X, player.Z) <= GameplayRules.PlatformRadiusSquared;
            if (player.Grounded && !inside) { player.Grounded = false; player.Vy = Fixed64.Zero; }
            if (!player.Grounded)
            {
                Fixed64 previousY = player.Y;
                player.Y += player.Vy;
                player.Vy -= GameplayRules.Gravity;
                if (previousY >= Fixed64.Zero && player.Y <= Fixed64.Zero && inside)
                {
                    player.Y = Fixed64.Zero;
                    player.Vy = Fixed64.Zero;
                    player.Grounded = true;
                }
            }
        }

        static Fixed64 DistanceSquared(Fixed64 x, Fixed64 z) => x * x + z * z;

        static void PushBodies(ref PlayerState first, ref PlayerState second)
        {
            if (first.Falling || second.Falling) return;
            Fixed64 dx = second.X - first.X, dz = second.Z - first.Z;
            Fixed64 squared = DistanceSquared(dx, dz);
            if (squared >= Fixed64.One) return;
            if (dx == Fixed64.Zero && dz == Fixed64.Zero)
            {
                first.X -= Fixed64.Half;
                second.X += Fixed64.Half;
                return;
            }
            // 非零的极小差值平方可能舍入为零，不能误判成完全重合或除以零。
            Fixed64 distance = squared == Fixed64.Zero
                ? new FixVector3(dx, Fixed64.Zero, dz).Length : Fixed64.Sqrt(squared);
            Fixed64 push = (Fixed64.One - distance) * Fixed64.Half;
            Fixed64 offsetX = dx / distance * push, offsetZ = dz / distance * push;
            first.X -= offsetX;
            first.Z -= offsetZ;
            second.X += offsetX;
            second.Z += offsetZ;
        }

        static bool CanHit(PlayerState attacker, PlayerState defender)
        {
            if (attacker.Action != PlayerAction.Attack || attacker.HitThisAttack
                || attacker.ActionFrame <= GameplayRules.AttackStartup
                || attacker.ActionFrame > GameplayRules.AttackStartup + GameplayRules.AttackActive) return false;
            Fixed64 vertical = defender.Y - attacker.Y;
            if (vertical <= GameplayRules.HitVerticalLower || vertical >= GameplayRules.HitVerticalUpper) return false;
            GameplayRules.Direction(attacker.FacingX, attacker.FacingZ, out Fixed64 dx, out Fixed64 dz);
            Fixed64 differenceX = defender.X - (attacker.X + dx * GameplayRules.HitboxOffset);
            Fixed64 differenceZ = defender.Z - (attacker.Z + dz * GameplayRules.HitboxOffset);
            return DistanceSquared(differenceX, differenceZ) <= GameplayRules.HitDistanceSquared;
        }

        static void ApplyHit(ref PlayerState defender, sbyte x, sbyte z)
        {
            GameplayRules.Direction(x, z, out Fixed64 dx, out Fixed64 dz);
            Fixed64 speed = Fixed64.FromInt(2 + defender.HitCount) / Fixed64.FromInt(GameplayRules.HitstunFrames);
            defender.KnockbackX = dx * speed;
            defender.KnockbackZ = dz * speed;
            defender.HitCount++;
            defender.Action = PlayerAction.Hitstun;
            defender.ActionFrame = 0;
        }

        static void AdvanceRound(ref GameState state)
        {
            state.PhaseFrame++;
            if (state.Phase == MatchPhase.Countdown && state.PhaseFrame >= GameplayRules.CountdownFrames)
            {
                state.Phase = MatchPhase.Fight;
                state.PhaseFrame = 0;
            }
            else if (state.Phase == MatchPhase.Fight)
            {
                bool firstOut = state.Player1.Y <= GameplayRules.OutY, secondOut = state.Player2.Y <= GameplayRules.OutY;
                if (firstOut || secondOut)
                    FinishRound(ref state, firstOut == secondOut ? RoundResult.Draw : firstOut ? RoundResult.Player2 : RoundResult.Player1);
                else if (state.PhaseFrame >= GameplayRules.FightFrames)
                {
                    int comparison = state.Player1.HitCount.CompareTo(state.Player2.HitCount);
                    if (comparison == 0)
                        comparison = DistanceSquared(state.Player1.X, state.Player1.Z).CompareTo(DistanceSquared(state.Player2.X, state.Player2.Z));
                    FinishRound(ref state, comparison == 0 ? RoundResult.Draw : comparison < 0 ? RoundResult.Player1 : RoundResult.Player2);
                }
            }
            else if (state.Phase == MatchPhase.Outro && state.PhaseFrame >= GameplayRules.OutroFrames)
            {
                state.PhaseFrame = 0;
                if (state.Player1Wins >= GameplayRules.WinsToMatch || state.Player2Wins >= GameplayRules.WinsToMatch)
                    state.Phase = MatchPhase.MatchOver;
                else
                {
                    state.RoundNumber++;
                    state.Phase = MatchPhase.Countdown;
                    ResetPlayers(ref state);
                }
            }
        }

        static void FinishRound(ref GameState state, RoundResult result)
        {
            state.LastRoundResult = result;
            if (result == RoundResult.Player1) state.Player1Wins++;
            if (result == RoundResult.Player2) state.Player2Wins++;
            state.Phase = MatchPhase.Outro;
            state.PhaseFrame = 0;
        }
    }
}
