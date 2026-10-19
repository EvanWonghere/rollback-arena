using Arena.Rollback;
using Arena.Sim;

namespace Arena.Headless;

public static class RollbackCli
{
    public static int Run(string[] args)
    {
        try
        {
            int distance = 8;
            ulong seed = 0;
            for (int i = 1; i < args.Length; i++)
            {
                if (i + 1 >= args.Length) throw new ArgumentException("Missing option value.");
                if (args[i] == "--distance") distance = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[i] == "--seed") seed = ulong.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else throw new ArgumentException($"Unknown rollback option: {args[i]}");
            }
            if (args[0] == "--sync-test")
            {
                MatchRun run = HeadlessMatch.Bots();
                ulong hash = SyncTestSession.Run(run.Inputs, distance, ArenaGame.Create());
                Console.WriteLine($"SYNC_TEST_OK frames={run.Inputs.Length} distance={distance} checksum={hash:X16}");
                return 0;
            }
            EquivalenceRun result = RollbackEquivalenceRunner.Run(seed);
            Console.WriteLine($"ROLLBACK_EQUIVALENT frames={result.Frames} ticks={result.NetworkTicks} rollbacks={result.Loads} resimulated={result.ResimulatedFrames} stalls={result.Stalls} maxRollback={result.MaxRollbackDistance} fight={result.FightFrames} inputEdges={result.FightInputEdges} sent={result.Network.Sent} delivered={result.Network.Delivered} dropped={result.Network.Dropped} duplicated={result.Network.Duplicated} checksum={result.FinalHash:X16}");
            return 0;
        }
        catch (RollbackException ex)
        {
            Console.Error.WriteLine($"{ex.Error}: frame={ex.Frame}: {ex.Message}");
            return 2;
        }
    }
}
