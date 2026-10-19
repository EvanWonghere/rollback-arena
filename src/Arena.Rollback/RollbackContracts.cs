using System;
using Arena.Sim;

namespace Arena.Rollback
{
    /// <summary>协议、历史或请求错误，不能通过静默覆盖或截断请求绕过。</summary>
    public enum RollbackError { HistoryOverflow, MissingHistory, FrameMismatch, BufferTooSmall, ConflictingInput, InvalidPacket, InvalidAck, QueueFull, Desync }

    /// <summary>携带错误类型及相关帧号；Frame=-1 表示没有特定帧上下文。</summary>
    public sealed class RollbackException : InvalidOperationException
    {
        public RollbackError Error { get; }
        public long Frame { get; }
        public RollbackException(RollbackError error, string message, long frame = -1) : base(message) { Error = error; Frame = frame; }
    }

    /// <summary>会话的不可变配置，构造时检查历史与请求容量能否容纳给定窗口。</summary>
    public sealed class SessionOptions
    {
        /// <summary>捕获帧到目标输入帧的固定延迟，不是网络链路延迟。</summary>
        public int InputDelayFrames { get; }
        /// <summary>允许新增游戏步后的未确认领先帧数；0 表示锁步等待实际输入。</summary>
        public int MaxPredictionFrames { get; }
        /// <summary>各个帧历史环形缓冲的槽位数，不是会话的总运行帧数上限。</summary>
        public int HistoryCapacity { get; }
        /// <summary>单批请求缓冲的容量，需容纳纠正与最多一个新步的请求。</summary>
        public int RequestCapacity { get; }
        /// <summary>双方约定的固定会话身份，用于拒绝其他会话的数据包。</summary>
        public ulong SessionId { get; }

        public SessionOptions(int inputDelayFrames = 2, int maxPredictionFrames = 8, int historyCapacity = 256, int requestCapacity = 32, ulong sessionId = 1)
        {
            if (inputDelayFrames < 0 || maxPredictionFrames < 0 || historyCapacity <= 0 || requestCapacity <= 0
                || (long)historyCapacity < (long)inputDelayFrames + maxPredictionFrames + 2
                || (long)requestCapacity < 2L * maxPredictionFrames + 3)
                throw new ArgumentOutOfRangeException(nameof(historyCapacity), "Invalid delay, prediction window, or buffer capacity.");
            InputDelayFrames = inputDelayFrames;
            MaxPredictionFrames = maxPredictionFrames;
            HistoryCapacity = historyCapacity;
            RequestCapacity = requestCapacity;
            SessionId = sessionId;
        }
    }

    /// <summary>一个目标输入帧及该帧的按住输入，不携带“是否预测”的含义。</summary>
    public readonly struct FramedInput
    {
        public readonly long Frame;
        public readonly PlayerInput Value;
        public FramedInput(long frame, PlayerInput value)
        {
            if (frame < 0 || ((byte)value & 128) != 0) throw new ArgumentOutOfRangeException(nameof(frame));
            Frame = frame;
            Value = value;
        }
    }

    /// <summary>
    /// Stored=新登记；Duplicate=仍保留的同帧相同实际值；
    /// IgnoredRetired=已确认且退出保留范围，无法再检查旧值冲突。
    /// </summary>
    public enum InputReceiveResult { Stored, Duplicate, IgnoredRetired }
    /// <summary>未新增游戏步的原因；停等不表示网络应停止或请求缓冲必定为空。</summary>
    public enum StallReason { None, NoLocalInput, PredictionLimit, Backpressure }
    /// <summary>本次请求批次是否新增一步；历史重模拟不计入 ForwardAdvanced。</summary>
    public readonly struct AdvanceResult
    {
        public readonly bool ForwardAdvanced;
        public readonly StallReason StallReason;
        public AdvanceResult(bool forwardAdvanced, StallReason stallReason = StallReason.None)
        { ForwardAdvanced = forwardAdvanced; StallReason = stallReason; }
    }

    /// <summary>累计统计的值副本，初始全部为 0，需与实际执行的请求轨迹一致。</summary>
    public readonly struct RollbackStatistics
    {
        // Rollbacks 对应恢复次数，ResimulatedFrames 对应重模拟步数。
        // Stalls 统计未新增一步的调用，即使该调用包含历史纠正。
        public readonly long Rollbacks, ResimulatedFrames, Stalls;
        /// <summary>实际恢复时，从当前状态帧退回快照帧的最大距离。</summary>
        public readonly int MaxRollbackDistance;
        public RollbackStatistics(long rollbacks, long resimulatedFrames, long stalls, int maxRollbackDistance)
        { Rollbacks = rollbacks; ResimulatedFrames = resimulatedFrames; Stalls = stalls; MaxRollbackDistance = maxRollbackDistance; }
    }

    /// <summary>
    /// 会话边界：登记输入、观测进度、生成请求。具体入口契约见 RollbackSession。
    /// 请求必须同步执行完整批次后，才能再次调用会话入口。
    /// </summary>
    public interface IRollbackSession
    {
        SessionOptions Options { get; }
        int LocalSlot { get; }
        long NextFrame { get; }
        long LastConfirmedInputFrame { get; }
        long RemoteContiguousInputFrame { get; }
        RollbackStatistics Statistics { get; }
        FramedInput CaptureLocalInput(long captureFrame, PlayerInput held);
        InputReceiveResult ReceiveRemoteInput(long targetFrame, PlayerInput held);
        AdvanceResult AdvanceFrame(RollbackRequestBuffer requests);
    }

    /// <summary>保存当前帧、恢复指定快照或执行指定输入步；执行器不补充缺失请求。</summary>
    public enum RollbackRequestKind : byte { SaveState, LoadState, Advance }
    /// <summary>一个不可变请求。状态帧 F 位于输入 F 执行前，Step(F) 后得到状态 F+1。</summary>
    public readonly struct RollbackRequest
    {
        public readonly RollbackRequestKind Kind;
        public readonly long Frame;
        public readonly FrameInputs Inputs;
        /// <summary>是否重走已执行过的帧；只有 Advance 请求使用，执行器会独立校验。</summary>
        public readonly bool IsResimulation;
        RollbackRequest(RollbackRequestKind kind, long frame, FrameInputs inputs, bool resimulation)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            Kind = kind; Frame = frame; Inputs = inputs; IsResimulation = resimulation;
        }
        public static RollbackRequest Save(long frame) => new RollbackRequest(RollbackRequestKind.SaveState, frame, default, false);
        public static RollbackRequest Load(long frame) => new RollbackRequest(RollbackRequestKind.LoadState, frame, default, false);
        public static RollbackRequest Step(long frame, FrameInputs inputs, bool resimulation = false) => new RollbackRequest(RollbackRequestKind.Advance, frame, inputs, resimulation);
    }

    /// <summary>固定容量的有序请求列表，调用者在每次 AdvanceFrame 前 Clear。</summary>
    /// <remarks>满时抛 BufferTooSmall，不扩容、不丢请求；Count 之外的索引不可读取。</remarks>
    public sealed class RollbackRequestBuffer
    {
        readonly RollbackRequest[] entries;
        public int Count { get; private set; }
        public int Capacity => entries.Length;
        public RollbackRequestBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            entries = new RollbackRequest[capacity];
        }
        public RollbackRequest this[int index] => index >= 0 && index < Count ? entries[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public void Add(RollbackRequest request)
        {
            if (Count == entries.Length) throw new RollbackException(RollbackError.BufferTooSmall, "Request buffer is full.");
            entries[Count++] = request;
        }
        /// <summary>仅将有效请求数清零，不推进会话、清除输入或退休快照。</summary>
        public void Clear() { Count = 0; }
    }
}
