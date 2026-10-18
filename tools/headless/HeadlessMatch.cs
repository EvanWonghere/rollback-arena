using System.Globalization;
using Arena.Sim;

namespace Arena.Headless;

public sealed class MatchRun
{
    public GameState FinalState { get; }
    public FrameInputs[] Inputs { get; }
    public ulong[] Hashes { get; }
    public int HitEvents { get; }
    public MatchRun(GameState state, FrameInputs[] inputs, ulong[] hashes, int hits)
    { FinalState = state; Inputs = inputs; Hashes = hashes; HitEvents = hits; }
}

public static class HeadlessMatch
{
    public static MatchRun Bots(int maxFrames = 30_000)
    {
        if (maxFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        var state = ArenaGame.Create();
        var inputs = new List<FrameInputs>();
        var hashes = new List<ulong>();
        int hits = 0;
        while (state.Phase != MatchPhase.MatchOver && inputs.Count < maxFrames)
        {
            var frame = new FrameInputs(Choose(state, true), Choose(state, false));
            inputs.Add(frame);
            hits += Advance(ref state, frame);
            hashes.Add(StateChecksum.Compute(state));
        }
        return new MatchRun(state, inputs.ToArray(), hashes.ToArray(), hits);
    }

    static PlayerInput Choose(in GameState state, bool first)
    {
        if (state.Phase != MatchPhase.Fight) return PlayerInput.None;
        PlayerState self = first ? state.Player1 : state.Player2;
        PlayerState target = first ? state.Player2 : state.Player1;
        Fixed64 dx = target.X - self.X, dz = target.Z - self.Z;
        PlayerInput input = PlayerInput.None;
        if (dx > Fixed64.Zero) input |= PlayerInput.Right;
        if (dx < Fixed64.Zero) input |= PlayerInput.Left;
        if (dz > Fixed64.Zero) input |= PlayerInput.Up;
        if (dz < Fixed64.Zero) input |= PlayerInput.Down;
        // 不同节奏打破完全镜像；输出仍只是普通按住输入，没有模拟作弊入口。
        int period = first ? 24 : 29;
        int offset = first ? 0 : 7;
        if (dx * dx + dz * dz <= Fixed64.FromInt(4) && state.PhaseFrame % period == offset)
            input |= PlayerInput.Attack;
        return input;
    }

    static int Advance(ref GameState state, FrameInputs input)
    {
        int first = state.Player1.HitCount, second = state.Player2.HitCount;
        ArenaGame.Step(ref state, input);
        return Math.Max(0, state.Player1.HitCount - first) + Math.Max(0, state.Player2.HitCount - second);
    }

    public static MatchRun Replay(IReadOnlyList<FrameInputs> inputs)
    {
        var state = ArenaGame.Create();
        var hashes = new ulong[inputs.Count];
        var copy = new FrameInputs[inputs.Count];
        int hits = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            copy[i] = inputs[i];
            hits += Advance(ref state, inputs[i]);
            hashes[i] = StateChecksum.Compute(state);
        }
        return new MatchRun(state, copy, hashes, hits);
    }

    public static void WriteInputs(string path, IReadOnlyList<FrameInputs> inputs)
    {
        using var writer = new StreamWriter(path) { NewLine = "\n" };
        writer.WriteLine("frame,p1,p2");
        for (int i = 0; i < inputs.Count; i++)
            writer.WriteLine(FormattableString.Invariant($"{i},{(byte)inputs[i].Player1},{(byte)inputs[i].Player2}"));
    }

    public static FrameInputs[] ReadInputs(string path)
    {
        using var reader = new StreamReader(path);
        if (reader.ReadLine() != "frame,p1,p2") throw new InvalidDataException("Expected replay header: frame,p1,p2");
        var inputs = new List<FrameInputs>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string[] cells = line.Split(',');
            if (cells.Length != 3 || !int.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out int frame)
                || frame != inputs.Count
                || !byte.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte first)
                || !byte.TryParse(cells[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte second))
                throw new InvalidDataException($"Invalid replay row {inputs.Count + 2}.");
            inputs.Add(new FrameInputs((PlayerInput)first, (PlayerInput)second));
        }
        if (inputs.Count == 0) throw new InvalidDataException("Replay contains no frames.");
        return inputs.ToArray();
    }

    public static void WriteHashes(string path, IReadOnlyList<ulong> hashes)
    {
        using var writer = new StreamWriter(path) { NewLine = "\n" };
        writer.WriteLine("frame,checksum");
        for (int i = 0; i < hashes.Count; i++)
            writer.WriteLine(FormattableString.Invariant($"{i + 1},{hashes[i]:X16}"));
    }
}
