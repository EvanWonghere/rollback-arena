using System;
using Arena.Sim;

namespace Arena.Rollback
{
    /// <summary>
    /// 确定性的双人回滚会话：登记真实输入，预测远端，生成保存/恢复/推进请求。
    /// 本轮按用户明确授权由 AI 完成剩余 S3；学习入口见 docs/s3-human-guide.md。
    /// </summary>
    /// <remarks>
    /// 会话不持有 GameState，也不直接调用 ArenaGame.Step。调用者必须完整执行
    /// 返回的请求批次，才可再次调用本会话；发生异常后不支持半批重试。
    /// </remarks>
    public sealed class RollbackSession : IRollbackSession
    {
        /// <summary>双方约定的固定配置。</summary>
        public SessionOptions Options { get; }
        /// <summary>本地全局槽位：0=P1，1=P2；不能把“本地”一律当作 Player1。</summary>
        public int LocalSlot { get; }
        /// <summary>按目标帧保存的本地真实输入；外部仅作观察，不应直接修改容器。</summary>
        public FrameRing<PlayerInput> LocalInputHistory { get; }
        /// <summary>按目标帧保存的远端真实输入，绝不混入预测值。</summary>
        public FrameRing<PlayerInput> RemoteInputHistory { get; }
        public long LastConfirmedInputFrameInternal { get; private set; } = -1;
        public long RemoteContiguousInputFrameInternal { get; private set; } = -1;
        public long NextFrameInternal { get; private set; }
        public RollbackStatistics StatisticsInternal { get; private set; }

        // 与真实输入历史分开：记住每一帧模拟时到底用了什么，供迟到实际值比较。
        readonly FrameRing<PlayerInput> remoteUsedInputs;
        // -1 表示没有待纠正；多个迟到错值取最早帧，不能取最后收到的帧。
        long pendingRollbackFrame = -1;
        // 已实际退休的输入下界。所有 < 此帧的记录都已移除，不再声称能核对冲突。
        long earliestRetainedInputFrame;
        // 上一批请求承诺的安全退休边界，下一次入口才应用。
        // 收包刚推进确认前沿时，旧预测可能尚未纠正，不能立即按新前沿丢历史。
        long deferredRetireBefore;

        public RollbackSession(SessionOptions options, int localSlot)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (localSlot < 0 || localSlot > 1) throw new ArgumentOutOfRangeException(nameof(localSlot));
            Options = options;
            LocalSlot = localSlot;
            LocalInputHistory = new FrameRing<PlayerInput>(options.HistoryCapacity);
            RemoteInputHistory = new FrameRing<PlayerInput>(options.HistoryCapacity);
            remoteUsedInputs = new FrameRing<PlayerInput>(options.HistoryCapacity);

            // 捕获 c 映射到 c+delay。此前的启动前缀是双方预先知道的实际 None，
            // 但游戏还未执行，所以确认可领先于 NextFrame=0。
            LastConfirmedInputFrameInternal = options.InputDelayFrames - 1L;
            RemoteContiguousInputFrameInternal = options.InputDelayFrames - 1L;
            for (long frame = 0; frame < options.InputDelayFrames; frame++)
            {
                LocalInputHistory.Store(frame, PlayerInput.None);
                RemoteInputHistory.Store(frame, PlayerInput.None);
            }
        }

        /// <summary>下一步要执行的输入帧；完整批次执行后等于 GameState.Frame。</summary>
        public long NextFrame => NextFrameInternal;
        /// <summary>双方实际输入从 0 起连续齐全的最后一帧，不代表纠正已执行。</summary>
        public long LastConfirmedInputFrame => LastConfirmedInputFrameInternal;
        /// <summary>远端实际输入从 0 起连续保存的前沿，仅供网络 ACK 使用。</summary>
        public long RemoteContiguousInputFrame => RemoteContiguousInputFrameInternal;
        public RollbackStatistics Statistics => StatisticsInternal;

        /// <summary>捕获本地按住值，只映射一次 delay；不推进游戏或生成按下沿。</summary>
        public FramedInput CaptureLocalInput(long captureFrame, PlayerInput held)
        {
            if (captureFrame < 0 || captureFrame > long.MaxValue - Options.InputDelayFrames - 1L)
                throw new ArgumentOutOfRangeException(nameof(captureFrame), "Target input and its successor frame must be representable.");
            ValidateInput(held);
            long targetFrame = captureFrame + Options.InputDelayFrames;
            ApplyDeferredRetirement();
            RequireRetainedWindow(targetFrame);

            bool hasFrame = LocalInputHistory.TryGet(targetFrame, out PlayerInput existing);
            if (hasFrame && existing != held)
                throw new RollbackException(RollbackError.ConflictingInput, "Local input conflict.", targetFrame);
            if (!hasFrame) LocalInputHistory.Store(targetFrame, held);
            // 本地可能晚于远端登记，因此两个入口都维护双方确认前沿。
            UpdateConfirmedInputFrame();
            return new FramedInput(targetFrame, held);
        }

        /// <summary>登记已映射好的远端实际输入；乱序、重复与预测错误是不同概念。</summary>
        public InputReceiveResult ReceiveRemoteInput(long targetFrame, PlayerInput held)
        {
            if (targetFrame < 0 || targetFrame == long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(targetFrame));
            ValidateInput(held);
            ApplyDeferredRetirement();
            if (targetFrame < earliestRetainedInputFrame) return InputReceiveResult.IgnoredRetired;
            RequireRetainedWindow(targetFrame);

            // 已存在的两个真实值互相矛盾才是协议冲突；预测值不同则应纠正。
            if (RemoteInputHistory.TryGet(targetFrame, out PlayerInput existing))
            {
                if (existing != held)
                    throw new RollbackException(RollbackError.ConflictingInput, "Remote input conflict.", targetFrame);
                return InputReceiveResult.Duplicate;
            }

            bool mismatch = false;
            if (targetFrame < NextFrameInternal)
            {
                if (!remoteUsedInputs.TryGet(targetFrame, out PlayerInput used))
                    throw new RollbackException(RollbackError.MissingHistory, "Missing simulated remote input.", targetFrame);
                mismatch = used != held;
            }
            RemoteInputHistory.Store(targetFrame, held);
            if (mismatch && (pendingRollbackFrame < 0 || targetFrame < pendingRollbackFrame))
                pendingRollbackFrame = targetFrame;

            while (RemoteInputHistory.TryGet(RemoteContiguousInputFrameInternal + 1, out _))
                RemoteContiguousInputFrameInternal++;
            UpdateConfirmedInputFrame();
            return InputReceiveResult.Stored;
        }

        /// <summary>先纠正旧时间线，再决定是否新增一步；停等也可能包含纠正请求。</summary>
        /// <remarks>调用者事先 Clear 缓冲，返回后立即同步执行完整批次。</remarks>
        public AdvanceResult AdvanceFrame(RollbackRequestBuffer requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            ApplyDeferredRetirement();
            long previousNextFrame = NextFrameInternal;

            // 本地绝不预测。窗口约束所有新步，包括当前远端值已到、但更早仍有缺口的情况。
            bool hasLocal = LocalInputHistory.TryGet(previousNextFrame, out _);
            StallReason reason = !hasLocal ? StallReason.NoLocalInput
                : previousNextFrame - (LastConfirmedInputFrameInternal + 1) >= Options.MaxPredictionFrames
                    ? StallReason.PredictionLimit : StallReason.None;
            bool forward = reason == StallReason.None;
            long replayCount = pendingRollbackFrame < 0 ? 0 : previousNextFrame - pendingRollbackFrame;

            // 最坏批次：Load + 每个重模拟帧的 Save/Step + 可选的一步 Save/Step。
            // 写请求、改已用输入或统计之前预检，不能留下半批请求。
            long required = (replayCount == 0 ? 0 : 1 + 2 * replayCount) + (forward ? 2 : 0);
            if (required > requests.Capacity - requests.Count)
                throw new RollbackException(RollbackError.BufferTooSmall, "Insufficient capacity for the complete rollback batch.", previousNextFrame);

            if (replayCount > 0)
            {
                long rollbackFrame = pendingRollbackFrame;
                requests.Add(RollbackRequest.Load(rollbackFrame));
                // 后继的旧预测属于错误时间线。保留起点和它的前驱，再依新时间线覆盖起点。
                remoteUsedInputs.DiscardAfter(rollbackFrame);
                for (long frame = rollbackFrame; frame < previousNextFrame; frame++)
                    AppendStep(requests, frame, true);
                pendingRollbackFrame = -1;

                RollbackStatistics stats = StatisticsInternal;
                int distance = checked((int)replayCount);
                StatisticsInternal = new RollbackStatistics(stats.Rollbacks + 1,
                    stats.ResimulatedFrames + replayCount, stats.Stalls,
                    distance > stats.MaxRollbackDistance ? distance : stats.MaxRollbackDistance);
            }

            if (forward)
            {
                AppendStep(requests, previousNextFrame, false);
                NextFrameInternal++;
            }
            else
            {
                RollbackStatistics stats = StatisticsInternal;
                StatisticsInternal = new RollbackStatistics(stats.Rollbacks, stats.ResimulatedFrames,
                    stats.Stalls + 1, stats.MaxRollbackDistance);
            }

            // 这里只排定候选，不马上删除。下一入口意味着调用方已完整执行本批请求。
            // 同时受已确认与已执行进度约束，并保留最后一帧输入作为后续预测种子。
            long safeInput = LastConfirmedInputFrameInternal < NextFrameInternal - 1
                ? LastConfirmedInputFrameInternal : NextFrameInternal - 1;
            deferredRetireBefore = safeInput < 0 ? 0 : safeInput;
            return new AdvanceResult(forward, reason);
        }

        /// <summary>生成指定帧的 Save/Step，并记住该时间线实际使用的远端值。</summary>
        void AppendStep(RollbackRequestBuffer requests, long frame, bool resimulation)
        {
            if (!LocalInputHistory.TryGet(frame, out PlayerInput localInput))
                throw new RollbackException(RollbackError.MissingHistory, "Cannot predict missing local input.", frame);
            PlayerInput remoteInput;
            if (!RemoteInputHistory.TryGet(frame, out remoteInput))
            {
                if (frame == 0) remoteInput = PlayerInput.None;
                else if (!remoteUsedInputs.TryGet(frame - 1, out remoteInput))
                    throw new RollbackException(RollbackError.MissingHistory, "Missing predecessor for remote prediction.", frame);
            }
            FrameInputs inputs = LocalSlot == 0
                ? new FrameInputs(localInput, remoteInput) : new FrameInputs(remoteInput, localInput);
            remoteUsedInputs.Store(frame, remoteInput);
            requests.Add(RollbackRequest.Save(frame));
            requests.Add(RollbackRequest.Step(frame, inputs, resimulation));
        }

        /// <summary>仅应用上一已完成批次的边界，不能使用刚收包但尚未纠正的新前沿。</summary>
        void ApplyDeferredRetirement()
        {
            if (deferredRetireBefore <= earliestRetainedInputFrame) return;
            LocalInputHistory.DiscardBefore(deferredRetireBefore);
            RemoteInputHistory.DiscardBefore(deferredRetireBefore);
            remoteUsedInputs.DiscardBefore(deferredRetireBefore);
            earliestRetainedInputFrame = deferredRetireBefore;
        }

        void RequireRetainedWindow(long frame)
        {
            if (frame < earliestRetainedInputFrame)
                throw new RollbackException(RollbackError.MissingHistory, "Input is older than retained history.", frame);
            // 用差值比较，避免下界加容量在 long 极值处溢出；右端始终排除。
            if (frame - earliestRetainedInputFrame >= Options.HistoryCapacity)
                throw new RollbackException(RollbackError.HistoryOverflow, "Input is beyond the retained window.", frame);
        }

        static void ValidateInput(PlayerInput held)
        {
            if (((byte)held & 128) != 0) throw new ArgumentOutOfRangeException(nameof(held));
        }

        void UpdateConfirmedInputFrame()
        {
            while (LocalInputHistory.TryGet(LastConfirmedInputFrameInternal + 1, out _)
                && RemoteInputHistory.TryGet(LastConfirmedInputFrameInternal + 1, out _))
                LastConfirmedInputFrameInternal++;
        }
    }
}
