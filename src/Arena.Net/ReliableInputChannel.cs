using System;
using Arena.Rollback;
using Arena.Sim;

namespace Arena.Net
{
    /// <summary>仅可靠搬运实际输入。ACK 前沿来自用户会话，不在此计算游戏确认或预测。</summary>
    public sealed class ReliableInputChannel
    {
        readonly SessionOptions options;
        readonly byte localSlot;
        readonly ITransport transport;
        readonly FrameRing<FramedInput> pending;
        readonly byte[] receiveBytes = new byte[InputPacketCodec.MaxPacketBytes];
        readonly byte[] sendBytes = new byte[InputPacketCodec.MaxPacketBytes];
        readonly FramedInput[] received = new FramedInput[InputPacketCodec.MaxInputs];
        readonly PlayerInput[] sending = new PlayerInput[InputPacketCodec.MaxInputs];
        long queuedThrough, sentThrough, acknowledgedThrough, advertisedThrough;
        public long PendingCount => queuedThrough - acknowledgedThrough;
        public long AcknowledgedThrough => acknowledgedThrough;

        public ReliableInputChannel(SessionOptions options, byte localSlot, ITransport transport)
        {
            if (options == null || transport == null) throw new ArgumentNullException(nameof(options));
            if (localSlot > 1) throw new ArgumentOutOfRangeException(nameof(localSlot));
            this.options = options; this.localSlot = localSlot; this.transport = transport;
            pending = new FrameRing<FramedInput>(options.HistoryCapacity);
            queuedThrough = sentThrough = acknowledgedThrough = advertisedThrough = options.InputDelayFrames - 1L;
        }
        public void QueueLocalInput(FramedInput input)
        {
            if (input.Frame < options.InputDelayFrames && input.Value != PlayerInput.None)
                throw new RollbackException(RollbackError.ConflictingInput, "Startup input prefix must be None.", input.Frame);
            if (input.Frame <= acknowledgedThrough) return;
            if (pending.TryGet(input.Frame, out FramedInput existing))
            {
                if (existing.Value != input.Value) throw new RollbackException(RollbackError.ConflictingInput, "Conflicting queued input.", input.Frame);
                return;
            }
            if (queuedThrough == long.MaxValue || input.Frame != queuedThrough + 1)
                throw new RollbackException(RollbackError.FrameMismatch, "Outgoing inputs must be contiguous target frames.", input.Frame);
            pending.Store(input.Frame, input);
            queuedThrough = input.Frame;
        }
        public void ReceiveAvailable(Action<FramedInput> onInput)
        {
            if (onInput == null) throw new ArgumentNullException(nameof(onInput));
            while (transport.TryReceive(receiveBytes, out int length))
            {
                InputPacketHeader header = InputPacketCodec.Decode(new ReadOnlySpan<byte>(receiveBytes, 0, length), options.SessionId, (byte)(1 - localSlot), received);
                if (header.AckThrough > sentThrough)
                    throw new RollbackException(RollbackError.InvalidAck, "Peer acknowledged input that was never sent.", header.AckThrough);
                if (header.AckThrough > acknowledgedThrough)
                {
                    acknowledgedThrough = header.AckThrough;
                    pending.DiscardBefore(acknowledgedThrough + 1);
                }
                for (int i = 0; i < header.Count; i++) onInput(received[i]);
            }
        }
        public void SendPending(long remoteActualThrough)
        {
            if (remoteActualThrough < advertisedThrough) throw new RollbackException(RollbackError.InvalidAck, "Local contiguous ACK frontier moved backwards.", remoteActualThrough);
            advertisedThrough = remoteActualThrough;
            int count = (int)(PendingCount < InputPacketCodec.MaxInputs ? PendingCount : InputPacketCodec.MaxInputs);
            long start = count == 0 ? 0 : acknowledgedThrough + 1;
            for (int i = 0; i < count; i++)
            {
                if (!pending.TryGet(start + i, out FramedInput input)) throw new RollbackException(RollbackError.MissingHistory, "Missing input for reliable send.", start + i);
                sending[i] = input.Value;
            }
            var header = new InputPacketHeader(options.SessionId, localSlot, start, count, remoteActualThrough);
            int length = InputPacketCodec.Encode(header, new ReadOnlySpan<PlayerInput>(sending, 0, count), sendBytes);
            transport.Send(new ReadOnlySpan<byte>(sendBytes, 0, length));
            if (count > 0 && start + count - 1 > sentThrough) sentThrough = start + count - 1;
        }
    }
}
