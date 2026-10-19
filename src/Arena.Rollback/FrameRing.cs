using System;

namespace Arena.Rollback
{
    /// <summary>纯带标签存储：不决定预测、确认、回滚或何时退休数据。</summary>
    /// <remarks>
    /// 帧号取模只用于选择数组槽位，读取时还须匹配完整帧号。
    /// 例如容量为 4 时，帧 0 和帧 4 使用同一槽，但不能被当成同一记录。
    /// T 必须是 unmanaged 值类型；存入和取出均复制值，不借出内部数组。
    /// </remarks>
    public sealed class FrameRing<T> where T : unmanaged
    {
        // -1 表示空槽，不能用 values 的默认值判断记录是否存在。
        // 对 PlayerInput 而言，默认的 None 本身就是一种有效输入。
        readonly long[] frames;
        readonly T[] values;
        /// <summary>固定槽位数，不是当前已保存记录数。</summary>
        public int Capacity => frames.Length;
        /// <summary>分配固定数组并将所有槽标记为空，不会预填启动输入。</summary>
        public FrameRing(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            frames = new long[capacity]; values = new T[capacity];
            for (int i = 0; i < capacity; i++) frames[i] = -1;
        }
        // 这里只计算物理槽位，该索引本身不能证明目标帧存在。
        int Index(long frame)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            return (int)(frame % frames.Length);
        }
        /// <summary>按完整帧号存储，禁止覆盖尚未退休的另一个帧。</summary>
        /// <remarks>
        /// 同一帧再次 Store 会替换旧值。本容器不判断输入冲突，调用方必须先检查
        /// “同帧相同值”还是“同帧不同实际值”，不能把 Store 当作冲突检测器。
        /// </remarks>
        public void Store(long frame, T value)
        {
            int index = Index(frame);
            if (frames[index] != -1 && frames[index] != frame)
                throw new RollbackException(RollbackError.HistoryOverflow, "Cannot overwrite a live frame slot.", frame);
            frames[index] = frame; values[index] = value;
        }
        /// <summary>完整帧标签匹配时返回 true 并复制值，缺失时返回 false。</summary>
        /// <remarks>
        /// 返回 false 时 out 值是 default，没有“输入已知”的含义。
        /// 对输入历史而言，只有返回 true 才证明该帧确实存着一份记录，包括 None。
        /// </remarks>
        public bool TryGet(long frame, out T value)
        {
            int index = Index(frame);
            if (frames[index] == frame) { value = values[index]; return true; }
            value = default; return false;
        }
        /// <summary>清除严格小于 frame 的记录，frame 自身仍保留。</summary>
        /// <remarks>退休边界由调用方决定，只有确认不再需要的数据才能丢弃。</remarks>
        public void DiscardBefore(long frame)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            for (int i = 0; i < frames.Length; i++) if (frames[i] >= 0 && frames[i] < frame) { frames[i] = -1; values[i] = default; }
        }
        /// <summary>清除严格大于 frame 的记录，frame 自身仍保留。</summary>
        /// <remarks>这是机械清理操作，不判断是否应回滚，也不选择回滚起点。</remarks>
        public void DiscardAfter(long frame)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            for (int i = 0; i < frames.Length; i++) if (frames[i] > frame) { frames[i] = -1; values[i] = default; }
        }
    }
}
