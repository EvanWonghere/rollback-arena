using System;
using Arena.Sim;

namespace Arena.Rollback
{
    public readonly struct InputPacketHeader
    {
        public readonly ulong SessionId;
        public readonly byte SenderSlot;
        public readonly long StartInputFrame, AckThrough;
        public readonly int Count;
        public InputPacketHeader(ulong sessionId, byte senderSlot, long startInputFrame, int count, long ackThrough)
        { SessionId = sessionId; SenderSlot = senderSlot; StartInputFrame = startInputFrame; Count = count; AckThrough = ackThrough; }
    }

    public static class InputPacketCodec
    {
        public const int HeaderBytes = 27, MaxInputs = 32, MaxPacketBytes = HeaderBytes + MaxInputs;
        public const byte Version = 1;

        public static int Encode(InputPacketHeader header, ReadOnlySpan<PlayerInput> inputs, Span<byte> output)
        {
            Validate(header);
            if (inputs.Length != header.Count || output.Length < HeaderBytes + header.Count)
                throw new RollbackException(RollbackError.InvalidPacket, "Packet payload or output length is invalid.");
            for (int i = 0; i < inputs.Length; i++) ValidateInput(inputs[i]);
            output[0] = Version;
            Write(output, 1, header.SessionId);
            output[9] = header.SenderSlot;
            Write(output, 10, (ulong)header.StartInputFrame);
            output[18] = (byte)header.Count;
            Write(output, 19, unchecked((ulong)header.AckThrough));
            for (int i = 0; i < inputs.Length; i++) output[HeaderBytes + i] = (byte)inputs[i];
            return HeaderBytes + header.Count;
        }

        public static InputPacketHeader Decode(ReadOnlySpan<byte> packet, ulong expectedSession, byte expectedSender, Span<FramedInput> inputs)
        {
            if (packet.Length < HeaderBytes || packet[0] != Version)
                throw new RollbackException(RollbackError.InvalidPacket, "Packet version or header length is invalid.");
            var header = new InputPacketHeader(Read(packet, 1), packet[9], unchecked((long)Read(packet, 10)), packet[18], unchecked((long)Read(packet, 19)));
            Validate(header);
            if (header.SessionId != expectedSession || header.SenderSlot != expectedSender
                || packet.Length != HeaderBytes + header.Count || inputs.Length < header.Count)
                throw new RollbackException(RollbackError.InvalidPacket, "Packet identity or payload length is invalid.");
            // 全包验证完成后才写 caller 缓冲，不能把半个非法包送入会话。
            for (int i = 0; i < header.Count; i++) ValidateInput((PlayerInput)packet[HeaderBytes + i]);
            for (int i = 0; i < header.Count; i++) inputs[i] = new FramedInput(header.StartInputFrame + i, (PlayerInput)packet[HeaderBytes + i]);
            return header;
        }

        static void Validate(InputPacketHeader header)
        {
            if (header.SenderSlot > 1 || header.Count < 0 || header.Count > MaxInputs || header.StartInputFrame < 0
                || header.AckThrough < -1 || (header.Count == 0 && header.StartInputFrame != 0)
                || (header.Count > 0 && header.StartInputFrame > long.MaxValue - header.Count))
                throw new RollbackException(RollbackError.InvalidPacket, "Packet frame range, slot or count is invalid.");
        }
        static void ValidateInput(PlayerInput input)
        {
            if (((byte)input & 128) != 0) throw new RollbackException(RollbackError.InvalidPacket, "Reserved input bit is set.");
        }
        static void Write(Span<byte> output, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) { output[offset + i] = (byte)value; value >>= 8; }
        }
        static ulong Read(ReadOnlySpan<byte> input, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)input[offset + i] << (i * 8);
            return value;
        }
    }
}
