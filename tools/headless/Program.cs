using Arena.Sim;

namespace Arena.Headless;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && (args[0] == "--sync-test" || args[0] == "--rollback-bots")) return RollbackCli.Run(args);
            bool bots = false;
            string? replay = null, record = null, hashes = null;
            int maxFrames = 30_000;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--bots": bots = true; break;
                    case "--replay": replay = Value(args, ref i); break;
                    case "--record": record = Value(args, ref i); break;
                    case "--hashes": hashes = Value(args, ref i); break;
                    case "--max-frames": maxFrames = int.Parse(Value(args, ref i), System.Globalization.CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }
            if (bots == (replay != null))
                throw new ArgumentException("Choose --bots or --replay <inputs.csv>.");
            MatchRun run = bots ? HeadlessMatch.Bots(maxFrames) : HeadlessMatch.Replay(HeadlessMatch.ReadInputs(replay!));
            if (record != null) HeadlessMatch.WriteInputs(record, run.Inputs);
            if (hashes != null) HeadlessMatch.WriteHashes(hashes, run.Hashes);
            GameState state = run.FinalState;
            string status = bots ? state.Phase == MatchPhase.MatchOver ? "MATCH_OVER" : "FRAME_LIMIT" : "REPLAY_OK";
            Console.WriteLine(FormattableString.Invariant($"{status} frames={state.Frame} score={state.Player1Wins}:{state.Player2Wins} rounds={state.RoundNumber} hits={run.HitEvents} checksum={StateChecksum.Compute(state):X16}"));
            return bots && state.Phase != MatchPhase.MatchOver ? 1 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is FormatException || ex is OverflowException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    static string Value(string[] args, ref int index)
    {
        if (++index >= args.Length) throw new ArgumentException("Missing option value.");
        return args[index];
    }
}
