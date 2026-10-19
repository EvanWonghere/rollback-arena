using System.Reflection;
using Arena.Net;
using Arena.Rollback;
using Arena.Sim;

namespace Arena.Headless;

public sealed class EquivalenceTrace
{
    readonly ulong[] expected;
    readonly GameState[] states;
    readonly int[] next = { 1, 1 };
    readonly bool[] initialChecked = new bool[2];
    public int EndFrame => expected.Length - 1;
    public int ConfirmedThrough(int peer) => next[peer] - 1;
    public int FightFrames { get; }
    public int FightInputEdges { get; }

    public EquivalenceTrace(FrameInputs[] captured, int delay)
    {
        if (captured.Length == 0 || delay < 0 || (long)captured.Length + delay >= int.MaxValue) throw new ArgumentOutOfRangeException(nameof(captured));
        expected = new ulong[captured.Length + delay + 1];
        states = new GameState[expected.Length];
        var state = ArenaGame.Create();
        states[0] = state; expected[0] = StateChecksum.Compute(state);
        int fights = 0, edges = 0;
        FrameInputs previous = default;
        for (int f = 0; f < EndFrame; f++)
        {
            FrameInputs held = f < delay ? default : captured[f - delay];
            if (state.Phase == MatchPhase.Fight)
            {
                fights++;
                int pressed = ((byte)held.Player1 & ~(byte)previous.Player1) | (((byte)held.Player2 & ~(byte)previous.Player2) << 8);
                while (pressed != 0) { edges += pressed & 1; pressed >>= 1; }
            }
            ArenaGame.Step(ref state, held);
            previous = held;
            states[f + 1] = state; expected[f + 1] = StateChecksum.Compute(state);
        }
        FightFrames = fights;
        FightInputEdges = edges;
    }

    public void Observe(int peer, int frame, ulong hash, GameState actual)
    {
        if (peer < 0 || peer > 1 || frame < 0 || frame > EndFrame || actual.Frame != frame)
            throw new InvalidOperationException("Invalid confirmation trace frame.");
        if (frame == 0)
        {
            if (initialChecked[peer]) throw new InvalidOperationException("Initial state was checked twice.");
            initialChecked[peer] = true;
        }
        else if (!initialChecked[peer] || frame != next[peer]) throw new InvalidOperationException("Confirmed trace has a gap or duplicate.");
        if (hash != StateChecksum.Compute(actual)) throw new InvalidOperationException("Cached hash does not match the supplied state.");
        if (expected[frame] != hash)
            throw new InvalidOperationException($"peer={peer}, stateFrame={frame}, expected={expected[frame]:X16}, actual={hash:X16}, firstDifference={FirstDifference(states[frame], actual)}");
        if (frame != 0) next[peer]++;
    }
    // Receive can advance C before correction requests run. Never publish or retire in that gap.
    public void PublishAfterBatch(int peer, long confirmed, RollbackRequestExecutor executor, bool batchExecuted)
    {
        if (!batchExecuted) return;
        if (confirmed < -1 || confirmed >= EndFrame) throw new InvalidOperationException("Invalid confirmation frontier.");
        long through = Math.Min(executor.State.Frame, confirmed + 1);
        for (int f = ConfirmedThrough(peer) + 1; f <= through; f++)
        {
            if (!executor.TryGetHash(f, out ulong hash) || !executor.TryGetState(f, out GameState state))
                throw new InvalidOperationException($"peer={peer}: missing confirmed state {f}.");
            Observe(peer, f, hash, state);
        }
        executor.RetireBefore(ConfirmedThrough(peer));
    }

    public bool CheckEndpointBatch(long before, AdvanceResult result, RollbackRequestBuffer requests)
    {
        if (before != EndFrame) return false;
        if (result.ForwardAdvanced || result.StallReason != StallReason.NoLocalInput)
            throw new InvalidOperationException("Endpoint must stall with NoLocalInput.");
        for (int i = 0; i < requests.Count; i++)
            if (requests[i].Kind == RollbackRequestKind.Advance && requests[i].Frame >= EndFrame)
                throw new InvalidOperationException("Endpoint must not execute another game step.");
        return true;
    }

    public void RequireComplete()
    {
        if (!initialChecked[0] || !initialChecked[1] || next[0] != expected.Length || next[1] != expected.Length)
            throw new InvalidOperationException($"Incomplete confirmation trace: {ConfirmedThrough(0)}/{ConfirmedThrough(1)} vs {EndFrame}.");
    }
    public static string FirstDifference(GameState expected, GameState actual) => Difference(expected, actual, "state") ?? "none (checksum contract mismatch)";
    static string? Difference(object expected, object actual, string path)
    {
        Type type = expected.GetType();
        if (type == typeof(Fixed64)) return ((Fixed64)expected).RawValue == ((Fixed64)actual).RawValue ? null : $"{path}: {((Fixed64)expected).RawValue} != {((Fixed64)actual).RawValue}";
        if (type.IsPrimitive || type.IsEnum) return expected.Equals(actual) ? null : $"{path}: {expected} != {actual}";
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string? difference = Difference(field.GetValue(expected)!, field.GetValue(actual)!, path + "." + field.Name);
            if (difference != null) return difference;
        }
        return null;
    }
}

public sealed class EquivalenceRun
{
    public int Frames { get; init; }
    public long NetworkTicks { get; init; }
    public long Loads { get; init; }
    public long ResimulatedFrames { get; init; }
    public long Stalls { get; init; }
    public int MaxRollbackDistance { get; init; }
    public int FightFrames { get; init; }
    public int FightInputEdges { get; init; }
    public NetworkStatistics Network { get; init; }
    public ulong FinalHash { get; init; }
}

public static class RollbackEquivalenceRunner
{
    // 所有阶段使用相同的复现信息；RollbackException 的帧号不在 Message 中，须显式保留。
    static InvalidOperationException Failure(ulong seed, long tick, int peer, string phase,
        SessionOptions options, RollbackRequestExecutor executor, RollbackRequestBuffer requests,
        InvalidOperationException error)
    {
        string details = error is RollbackException rollback
            ? $"{rollback.Error}, inputFrame={rollback.Frame}: {rollback.Message}"
            : error.Message;
        return new InvalidOperationException(
            $"seed={seed}, tick={tick}, peer={peer}, phase={phase}, stateFrame={executor.State.Frame}, delay={options.InputDelayFrames}, prediction={options.MaxPredictionFrames}, requests=[{RequestTrace(requests)}]: {details}", error);
    }

    static string RequestTrace(RollbackRequestBuffer buffer)
    {
        var parts = new string[buffer.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            RollbackRequest r = buffer[i];
            parts[i] = $"{r.Kind}({r.Frame},p1={(byte)r.Inputs.Player1},p2={(byte)r.Inputs.Player2},resim={r.IsResimulation})";
        }
        return string.Join(";", parts);
    }

    public static FrameInputs[] Corpus(ulong seed, int count)
    {
        if (count < 1000) throw new ArgumentOutOfRangeException(nameof(count), "Keep real fight frames and several ring wraps.");
        var inputs = new FrameInputs[count];
        var random = new DeterministicRandom(seed);
        if (seed % 3 == 0)
        {
            FrameInputs[] bots = HeadlessMatch.Bots().Inputs;
            Array.Copy(bots, inputs, Math.Min(bots.Length, inputs.Length));
        }
        else
            for (int i = 0; i < count; i++) inputs[i] = new FrameInputs((PlayerInput)(random.NextUInt64() & 127), (PlayerInput)(random.NextUInt64() & 127));
        return inputs;
    }

    public static EquivalenceRun Run(ulong seed, int captureFrames = 2400, Func<SessionOptions, int, IRollbackSession>? createSession = null)
    {
        var options = new SessionOptions((int)(seed % 3) * 2, sessionId: seed + 1);
        FrameInputs[] input = Corpus(seed, captureFrames);
        var oracle = new EquivalenceTrace(input, options.InputDelayFrames);
        var firstCondition = new NetworkConditions((int)(seed % 13), 3, 100, 100, 120, 6);
        var secondCondition = new NetworkConditions((int)((seed * 7) % 13), 3, 100, 100, 240, 5);
        var network = new SimulatedNetwork(seed ^ 0xACCE5510UL, firstCondition, secondCondition);
        createSession ??= (config, slot) => new RollbackSession(config, slot);
        IRollbackSession[] sessions = { createSession(options, 0), createSession(options, 1) };
        var executors = new[] { new RollbackRequestExecutor(ArenaGame.Create(), options.HistoryCapacity), new RollbackRequestExecutor(ArenaGame.Create(), options.HistoryCapacity) };
        var channels = new[] { new ReliableInputChannel(options, 0, network.First), new ReliableInputChannel(options, 1, network.Second) };
        var buffers = new[] { new RollbackRequestBuffer(options.RequestCapacity), new RollbackRequestBuffer(options.RequestCapacity) };
        long[] lastCaptured = { -1, -1 };
        long[] stalls = { 0, 0 };
        bool[] endpointChecked = new bool[2];
        for (int peer = 0; peer < 2; peer++) oracle.Observe(peer, 0, StateChecksum.Compute(executors[peer].State), executors[peer].State);
        long tickLimit = oracle.EndFrame * 50L + 1000;
        for (long tick = 0; tick < tickLimit; tick++)
        {
            network.AdvanceTo(tick);
            // 先捕获并发送双方输入，再轮询；零网络延迟不额外注入一个人为 tick 延迟。
            for (int peer = 0; peer < 2; peer++)
            {
                try
                {
                    long frame = sessions[peer].NextFrame;
                    if (frame != executors[peer].State.Frame || frame < 0 || frame > oracle.EndFrame)
                        throw new InvalidOperationException("Session/game frames differ before batch.");
                    if (frame < captureFrames && frame != lastCaptured[peer])
                    {
                        PlayerInput held = peer == 0 ? input[frame].Player1 : input[frame].Player2;
                        channels[peer].QueueLocalInput(sessions[peer].CaptureLocalInput(frame, held));
                        lastCaptured[peer] = frame;
                    }
                    channels[peer].SendPending(sessions[peer].RemoteContiguousInputFrame);
                }
                catch (InvalidOperationException ex)
                {
                    throw Failure(seed, tick, peer, "capture/send", options, executors[peer], buffers[peer], ex);
                }
            }
            for (int peer = 0; peer < 2; peer++)
            {
                try
                {
                    int slot = peer;
                    channels[peer].ReceiveAvailable(value => sessions[slot].ReceiveRemoteInput(value.Frame, value.Value));
                }
                catch (InvalidOperationException ex)
                {
                    throw Failure(seed, tick, peer, "receive", options, executors[peer], buffers[peer], ex);
                }
            }
            for (int peer = 0; peer < 2; peer++)
            {
                IRollbackSession session = sessions[peer];
                // 一端偶尔少跑一次，验证游戏与网络 tick 分开；这不是生产时间同步策略。
                bool executed = !(peer == 1 && tick % 5 == 0);
                try
                {
                    if (executed)
                    {
                        long before = session.NextFrame;
                        buffers[peer].Clear();
                        AdvanceResult result = session.AdvanceFrame(buffers[peer]);
                        endpointChecked[peer] |= oracle.CheckEndpointBatch(before, result, buffers[peer]);
                        executors[peer].Execute(buffers[peer]);
                        long expectedFrame = before + (result.ForwardAdvanced ? 1 : 0);
                        if (session.NextFrame != expectedFrame || executors[peer].State.Frame != expectedFrame || expectedFrame > oracle.EndFrame)
                            throw new InvalidOperationException("Invalid frame advancement.");
                        if (!result.ForwardAdvanced) stalls[peer]++;
                    }
                    oracle.PublishAfterBatch(peer, session.LastConfirmedInputFrame, executors[peer], executed);
                }
                catch (InvalidOperationException ex)
                {
                    throw Failure(seed, tick, peer, "advance", options, executors[peer], buffers[peer], ex);
                }
            }
            if (sessions[0].NextFrame == oracle.EndFrame && sessions[1].NextFrame == oracle.EndFrame
                && sessions[0].LastConfirmedInputFrame == oracle.EndFrame - 1 && sessions[1].LastConfirmedInputFrame == oracle.EndFrame - 1
                && channels[0].PendingCount == 0 && channels[1].PendingCount == 0
                && endpointChecked[0] && endpointChecked[1]
                && oracle.ConfirmedThrough(0) == oracle.EndFrame && oracle.ConfirmedThrough(1) == oracle.EndFrame)
            {
                oracle.RequireComplete();
                for (int peer = 0; peer < 2; peer++)
                {
                    RollbackStatistics stats = sessions[peer].Statistics;
                    if (stats.Rollbacks != executors[peer].ExecutedLoads || stats.ResimulatedFrames != executors[peer].ExecutedResimulations
                        || stats.MaxRollbackDistance != executors[peer].MaxLoadDistance || stats.Stalls != stalls[peer])
                        throw new InvalidOperationException($"seed={seed}, tick={tick}, peer={peer}: session statistics disagree with executed request trace.");
                }
                long loads = executors[0].ExecutedLoads + executors[1].ExecutedLoads;
                if (oracle.FightFrames == 0 || (firstCondition.DelayTicks > options.InputDelayFrames + 2 && loads == 0))
                    throw new InvalidOperationException("Fuzz case did not exercise required fight/correction coverage.");
                return new EquivalenceRun
                {
                    Frames = oracle.EndFrame, NetworkTicks = tick + 1, Loads = loads,
                    ResimulatedFrames = executors[0].ExecutedResimulations + executors[1].ExecutedResimulations,
                    Stalls = stalls[0] + stalls[1], FightFrames = oracle.FightFrames, FightInputEdges = oracle.FightInputEdges,
                    MaxRollbackDistance = Math.Max(executors[0].MaxLoadDistance, executors[1].MaxLoadDistance), Network = network.Statistics,
                    FinalHash = StateChecksum.Compute(executors[0].State)
                };
            }
        }
        throw new InvalidOperationException($"seed={seed}, tickLimit={tickLimit}: did not confirm full endpoint {oracle.EndFrame}; states={sessions[0].NextFrame}/{sessions[1].NextFrame}, confirmed={sessions[0].LastConfirmedInputFrame}/{sessions[1].LastConfirmedInputFrame}");
    }
}
