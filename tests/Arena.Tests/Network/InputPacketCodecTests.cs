using System;
using System.Linq;
using Arena.Rollback;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Network;

public class InputPacketCodecTests
{
    [Test]
    public void PacketUsesIndependentLittleEndianGoldenBytes()
    {
        var header = new InputPacketHeader(0x0102030405060708UL, 1, 256, 2, -1);
        var buffer = new byte[InputPacketCodec.MaxPacketBytes];
        int size = InputPacketCodec.Encode(header, new[] { PlayerInput.Attack, PlayerInput.Right }, buffer);
        byte[] expected = { 1, 8, 7, 6, 5, 4, 3, 2, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 2, 255, 255, 255, 255, 255, 255, 255, 255, 16, 8 };
        Assert.That(buffer.Take(size), Is.EqualTo(expected));
        var inputs = new FramedInput[32];
        var decoded = InputPacketCodec.Decode(expected, header.SessionId, 1, inputs);
        Assert.That(decoded.StartInputFrame, Is.EqualTo(256));
        Assert.That(decoded.AckThrough, Is.EqualTo(-1));
        Assert.That(inputs[0].Frame, Is.EqualTo(256));
        Assert.That(inputs[1].Frame, Is.EqualTo(257));
        Assert.That(inputs[0].Value, Is.EqualTo(PlayerInput.Attack));
        Assert.That(inputs[1].Value, Is.EqualTo(PlayerInput.Right));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(32)]
    public void PacketBoundsRoundTrip(int count)
    {
        var buffer = new byte[59];
        var values = Enumerable.Range(0, count).Select(i => (PlayerInput)(i & 127)).ToArray();
        var header = new InputPacketHeader(99, 0, count == 0 ? 0 : 12345, count, 234);
        int length = InputPacketCodec.Encode(header, values, buffer);
        var inputs = new FramedInput[32];
        var result = InputPacketCodec.Decode(buffer.AsSpan(0, length), 99, 0, inputs);
        Assert.That(result.Count, Is.EqualTo(count));
        Assert.That(result.AckThrough, Is.EqualTo(234));
        for (int i = 0; i < count; i++) Assert.That(inputs[i].Value, Is.EqualTo(values[i]));
    }

    [Test]
    public void InvalidPacketsCannotPartiallyWriteInputs()
    {
        var bytes = new byte[29];
        InputPacketCodec.Encode(new InputPacketHeader(1, 0, 0, 2, -1), new[] { PlayerInput.Right, PlayerInput.Attack }, bytes);
        var input = new[] { new FramedInput(77, PlayerInput.Jump), new FramedInput(78, PlayerInput.Jump) };
        bytes[28] = 128;
        Assert.Throws<RollbackException>(() => InputPacketCodec.Decode(bytes, 1, 0, input));
        Assert.That(input[0].Frame, Is.EqualTo(77));
        Assert.That(input[1].Frame, Is.EqualTo(78));
        bytes[28] = 16;
        Assert.Throws<RollbackException>(() => InputPacketCodec.Decode(bytes, 2, 0, input));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Decode(bytes, 1, 1, input));
        bytes[0] = 2;
        Assert.Throws<RollbackException>(() => InputPacketCodec.Decode(bytes, 1, 0, input));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Decode(bytes.AsSpan(0, 10), 1, 0, input));
    }

    [Test]
    public void InvalidHeadersAndBuffersAreRejected()
    {
        var buffer = new byte[59];
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 0, long.MaxValue, 1, -1), new[] { PlayerInput.None }, buffer));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 0, 1, 0, -1), Array.Empty<PlayerInput>(), buffer));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 0, 0, 0, -2), Array.Empty<PlayerInput>(), buffer));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 2, 0, 0, -1), Array.Empty<PlayerInput>(), buffer));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 0, 0, 33, -1), new PlayerInput[33], buffer));
        Assert.Throws<RollbackException>(() => InputPacketCodec.Encode(new InputPacketHeader(1, 0, 0, 1, -1), new[] { PlayerInput.None }, new byte[27]));
    }
}
