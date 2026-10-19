using System;
using Arena.Sim;

namespace Arena.Rollback
{
    /// <summary>V4 固定验证脚本：没有远端、预测或生产调度决策。</summary>
    public static class SyncTestSession
    {
        public static ulong Run(FrameInputs[] inputs, int distance, GameState initial)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Length == 0 || distance <= 0 || distance > 256) throw new ArgumentOutOfRangeException(nameof(distance));
            int capacity = distance + 2;
            var executor = new RollbackRequestExecutor(initial, capacity);
            var originals = new FrameRing<ulong>(capacity);
            var expected = new ulong[distance];
            long start = initial.Frame;
            for (int index = 0; index < inputs.Length; index++)
            {
                long frame = start + index;
                executor.Execute(RollbackRequest.Save(frame));
                executor.Execute(RollbackRequest.Step(frame, inputs[index]));
                originals.Store(frame + 1, StateChecksum.Compute(executor.State));
                int count = index + 1 < distance ? index + 1 : distance;
                long rewind = frame + 1 - count;
                // 原始结果先复制到独立数组，Load/重模拟覆盖哈希后仍有独立比较对象。
                for (int offset = 0; offset < count; offset++)
                    if (!originals.TryGet(rewind + offset + 1, out expected[offset]))
                        throw new RollbackException(RollbackError.MissingHistory, "Missing original SyncTest hash.");
                executor.Execute(RollbackRequest.Load(rewind));
                for (int offset = 0; offset < count; offset++)
                {
                    long replayFrame = rewind + offset;
                    executor.Execute(RollbackRequest.Save(replayFrame));
                    executor.Execute(RollbackRequest.Step(replayFrame, inputs[(int)(replayFrame - start)], true));
                    if (StateChecksum.Compute(executor.State) != expected[offset])
                        throw new RollbackException(RollbackError.Desync, "SyncTest resimulation differs from original state.", replayFrame + 1);
                }
                long retain = frame + 2 - distance;
                if (retain < start) retain = start;
                executor.RetireBefore(retain);
                originals.DiscardBefore(retain);
            }
            return StateChecksum.Compute(executor.State);
        }
    }
}
