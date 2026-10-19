using System;
using Arena.Rollback;
using Arena.Sim;

namespace Arena.Net
{
    public interface ITransport
    {
        void Send(ReadOnlySpan<byte> packet);
        bool TryReceive(Span<byte> output, out int length);
    }

    public readonly struct NetworkConditions
    {
        public readonly int DelayTicks, JitterTicks, LossPermille, DuplicatePermille;
        public readonly long BurstStart, BurstLength;
        public NetworkConditions(int delayTicks = 0, int jitterTicks = 0, int lossPermille = 0, int duplicatePermille = 0, long burstStart = 0, long burstLength = 0)
        {
            if (delayTicks < 0 || jitterTicks < 0 || (long)delayTicks + jitterTicks > int.MaxValue
                || lossPermille < 0 || lossPermille > 1000 || duplicatePermille < 0 || duplicatePermille > 1000
                || burstStart < 0 || burstLength < 0 || burstStart > long.MaxValue - burstLength)
                throw new ArgumentOutOfRangeException(nameof(delayTicks));
            DelayTicks = delayTicks; JitterTicks = jitterTicks; LossPermille = lossPermille; DuplicatePermille = duplicatePermille;
            BurstStart = burstStart; BurstLength = burstLength;
        }
    }

    public readonly struct NetworkStatistics
    {
        public readonly long Sent, Delivered, Dropped, Duplicated;
        public NetworkStatistics(long sent, long delivered, long dropped, long duplicated)
        { Sent = sent; Delivered = delivered; Dropped = dropped; Duplicated = duplicated; }
    }

    /// <summary>显式 tick、有限队列、固定种子；不会读取真实时间。</summary>
    public sealed class SimulatedNetwork
    {
        struct Packet
        {
            public bool Used;
            public int Destination, Length;
            public long Due, Ordinal;
        }
        sealed class Endpoint : ITransport
        {
            readonly SimulatedNetwork network;
            readonly int slot;
            public Endpoint(SimulatedNetwork network, int slot) { this.network = network; this.slot = slot; }
            public void Send(ReadOnlySpan<byte> packet) { network.Send(slot, packet); }
            public bool TryReceive(Span<byte> output, out int length) => network.Receive(slot, output, out length);
        }
        readonly Packet[] packets;
        readonly byte[] bytes;
        readonly NetworkConditions first, second;
        DeterministicRandom random;
        long ordinal, sent, delivered, dropped, duplicated;
        public long Tick { get; private set; }
        public ITransport First { get; }
        public ITransport Second { get; }
        public NetworkStatistics Statistics => new NetworkStatistics(sent, delivered, dropped, duplicated);

        public SimulatedNetwork(ulong seed, NetworkConditions first, NetworkConditions second, int packetCapacity = 256)
        {
            if (packetCapacity <= 0 || packetCapacity > int.MaxValue / InputPacketCodec.MaxPacketBytes)
                throw new ArgumentOutOfRangeException(nameof(packetCapacity));
            packets = new Packet[packetCapacity];
            bytes = new byte[packetCapacity * InputPacketCodec.MaxPacketBytes];
            this.first = first; this.second = second; random = new DeterministicRandom(seed);
            First = new Endpoint(this, 0); Second = new Endpoint(this, 1);
        }
        public void AdvanceTo(long tick)
        {
            if (tick < Tick) throw new ArgumentOutOfRangeException(nameof(tick));
            Tick = tick;
        }
        void Send(int source, ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 0 || payload.Length > InputPacketCodec.MaxPacketBytes) throw new ArgumentOutOfRangeException(nameof(payload));
            NetworkConditions conditions = source == 0 ? first : second;
            sent++;
            if ((Tick >= conditions.BurstStart && Tick - conditions.BurstStart < conditions.BurstLength)
                || random.NextInt(1000) < conditions.LossPermille) { dropped++; return; }
            int copies = random.NextInt(1000) < conditions.DuplicatePermille ? 2 : 1;
            int free = 0;
            for (int i = 0; i < packets.Length; i++) if (!packets[i].Used) free++;
            if (free < copies) throw new RollbackException(RollbackError.QueueFull, "Simulated packet queue is full.");
            if (copies == 2) duplicated++;
            for (int copy = 0; copy < copies; copy++)
            {
                int index = 0;
                while (packets[index].Used) index++;
                long jitter = conditions.JitterTicks == 0 ? 0 : (long)(random.NextUInt64() % ((ulong)conditions.JitterTicks + 1));
                long delay = conditions.DelayTicks + jitter;
                if (Tick > long.MaxValue - delay) throw new ArgumentOutOfRangeException(nameof(Tick));
                payload.CopyTo(new Span<byte>(bytes, index * InputPacketCodec.MaxPacketBytes, payload.Length));
                packets[index] = new Packet { Used = true, Destination = 1 - source, Length = payload.Length, Due = Tick + delay, Ordinal = ordinal++ };
            }
        }
        bool Receive(int destination, Span<byte> output, out int length)
        {
            int selected = -1;
            for (int i = 0; i < packets.Length; i++)
            {
                Packet packet = packets[i];
                if (!packet.Used || packet.Destination != destination || packet.Due > Tick) continue;
                if (selected == -1 || packet.Due < packets[selected].Due
                    || (packet.Due == packets[selected].Due && packet.Ordinal < packets[selected].Ordinal)) selected = i;
            }
            if (selected < 0) { length = 0; return false; }
            length = packets[selected].Length;
            if (output.Length < length) throw new ArgumentException("Receive buffer is too small.", nameof(output));
            new ReadOnlySpan<byte>(bytes, selected * InputPacketCodec.MaxPacketBytes, length).CopyTo(output);
            packets[selected].Used = false;
            delivered++;
            return true;
        }
    }
}
