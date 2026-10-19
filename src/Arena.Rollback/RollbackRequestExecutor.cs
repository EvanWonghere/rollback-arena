using Arena.Sim;

namespace Arena.Rollback
{
    /// <summary>只执行给定请求，不生成请求、不选择纠正起点或输入。</summary>
    public sealed class RollbackRequestExecutor
    {
        GameState state;
        long furthestFrame;
        readonly FrameRing<GameSnapshot> snapshots;
        readonly FrameRing<ulong> hashes;
        public GameState State => state;
        public long ExecutedLoads { get; private set; }
        public long ExecutedResimulations { get; private set; }
        public long ExecutedForwards { get; private set; }
        public int MaxLoadDistance { get; private set; }

        public RollbackRequestExecutor(GameState initial, int capacity)
        {
            if (initial.Frame < 0) throw new System.ArgumentOutOfRangeException(nameof(initial));
            state = initial;
            furthestFrame = initial.Frame;
            snapshots = new FrameRing<GameSnapshot>(capacity);
            hashes = new FrameRing<ulong>(capacity);
            hashes.Store(state.Frame, StateChecksum.Compute(state));
        }
        public void Execute(RollbackRequestBuffer requests)
        {
            for (int i = 0; i < requests.Count; i++) Execute(requests[i]);
        }
        public void Execute(RollbackRequest request)
        {
            switch (request.Kind)
            {
                case RollbackRequestKind.SaveState:
                    RequireFrame(request.Frame);
                    snapshots.Store(request.Frame, new GameSnapshot(state));
                    hashes.Store(request.Frame, StateChecksum.Compute(state));
                    break;
                case RollbackRequestKind.LoadState:
                    if (request.Frame > state.Frame || !snapshots.TryGet(request.Frame, out GameSnapshot saved))
                        throw new RollbackException(RollbackError.MissingHistory, "Cannot load missing or future state.", request.Frame);
                    long distance = state.Frame - request.Frame;
                    saved.Restore(ref state);
                    RequireFrame(request.Frame);
                    snapshots.DiscardAfter(request.Frame);
                    hashes.DiscardAfter(request.Frame);
                    ExecutedLoads++;
                    if (distance > MaxLoadDistance) MaxLoadDistance = checked((int)distance);
                    break;
                case RollbackRequestKind.Advance:
                    RequireFrame(request.Frame);
                    if (!snapshots.TryGet(request.Frame, out _))
                        throw new RollbackException(RollbackError.MissingHistory, "Advance requires a saved pre-step state.", request.Frame);
                    if (request.IsResimulation != (request.Frame < furthestFrame))
                        throw new RollbackException(RollbackError.FrameMismatch, "Resimulation flag differs from executed frame history.", request.Frame);
                    ArenaGame.Step(ref state, request.Inputs);
                    if (state.Frame > furthestFrame) furthestFrame = state.Frame;
                    hashes.Store(state.Frame, StateChecksum.Compute(state));
                    if (request.IsResimulation) ExecutedResimulations++; else ExecutedForwards++;
                    break;
                default: throw new RollbackException(RollbackError.FrameMismatch, "Unknown rollback request.", request.Frame);
            }
        }
        void RequireFrame(long frame)
        {
            if (state.Frame != frame) throw new RollbackException(RollbackError.FrameMismatch, "Request frame differs from game state.", frame);
        }
        public bool TryGetHash(long frame, out ulong hash) => hashes.TryGet(frame, out hash);
        public bool TryGetState(long frame, out GameState value)
        {
            if (frame == state.Frame) { value = state; return true; }
            if (snapshots.TryGet(frame, out GameSnapshot saved)) { value = default; saved.Restore(ref value); return true; }
            value = default; return false;
        }
        public void RetireBefore(long frame) { snapshots.DiscardBefore(frame); hashes.DiscardBefore(frame); }
    }
}
