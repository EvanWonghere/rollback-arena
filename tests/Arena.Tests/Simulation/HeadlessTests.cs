using System;
using System.IO;
using Arena.Headless;
using Arena.Sim;
using NUnit.Framework;

namespace Arena.Tests.Simulation;

public class HeadlessTests
{
    [Test]
    public void BotsFinishRealCombatAndReplayMatchesEveryHash()
    {
        MatchRun run = HeadlessMatch.Bots();
        Assert.Multiple(() =>
        {
            Assert.That(run.FinalState.Phase, Is.EqualTo(MatchPhase.MatchOver));
            Assert.That(Math.Max(run.FinalState.Player1Wins, run.FinalState.Player2Wins), Is.EqualTo(2));
            Assert.That(run.HitEvents, Is.GreaterThan(0), "must fight rather than only walk out");
            Assert.That(run.Inputs.Length, Is.GreaterThan(90));
            Assert.That(run.Inputs.Length, Is.LessThan(30_000));
            Assert.That(run.Hashes.Length, Is.EqualTo(run.Inputs.Length));
        });
        MatchRun replay = HeadlessMatch.Replay(run.Inputs);
        Assert.That(replay.Hashes, Is.EqualTo(run.Hashes));
        Assert.That(replay.FinalState, Is.EqualTo(run.FinalState));
        Assert.That(replay.HitEvents, Is.EqualTo(run.HitEvents));
        TestContext.Out.WriteLine($"bots frames={run.FinalState.Frame}, score={run.FinalState.Player1Wins}:{run.FinalState.Player2Wins}, rounds={run.FinalState.RoundNumber}, hits={run.HitEvents}, hash={StateChecksum.Compute(run.FinalState):X16}");
    }

    [Test]
    public void FrameBudgetDoesNotPretendToFinishAMatch()
    {
        MatchRun run = HeadlessMatch.Bots(1);
        Assert.That(run.FinalState.Frame, Is.EqualTo(1));
        Assert.That(run.FinalState.Phase, Is.Not.EqualTo(MatchPhase.MatchOver));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Bots(0));
    }

    [Test]
    public void ReplayFilesRoundTripAndHashRowsMatchFrameNumbers()
    {
        var directory = Directory.CreateTempSubdirectory("arena-replay-");
        try
        {
            MatchRun run = HeadlessMatch.Bots();
            string inputs = Path.Combine(directory.FullName, "inputs.csv");
            string hashes = Path.Combine(directory.FullName, "hashes.csv");
            HeadlessMatch.WriteInputs(inputs, run.Inputs);
            HeadlessMatch.WriteHashes(hashes, run.Hashes);
            FrameInputs[] loaded = HeadlessMatch.ReadInputs(inputs);
            Assert.That(loaded, Is.EqualTo(run.Inputs));
            Assert.That(HeadlessMatch.Replay(loaded).Hashes, Is.EqualTo(run.Hashes));
            string[] lines = File.ReadAllLines(hashes);
            Assert.That(lines.Length, Is.EqualTo(run.Hashes.Length + 1));
            Assert.That(lines[0], Is.EqualTo("frame,checksum"));
            Assert.That(lines[1], Is.EqualTo($"1,{run.Hashes[0]:X16}"));
            Assert.That(lines[^1], Is.EqualTo($"{run.Inputs.Length},{run.Hashes[^1]:X16}"));
        }
        finally { directory.Delete(true); }
    }

    [TestCase("wrong\n0,0,0\n")]
    [TestCase("frame,p1,p2\n")]
    [TestCase("frame,p1,p2\n1,0,0\n")]
    [TestCase("frame,p1,p2\n0,256,0\n")]
    [TestCase("frame,p1,p2\n0,-1,0\n")]
    [TestCase("frame,p1,p2\n0,0,0,0\n")]
    public void MalformedOrEmptyReplayIsRejected(string content)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            Assert.Throws<InvalidDataException>(() => HeadlessMatch.ReadInputs(path));
        }
        finally { File.Delete(path); }
    }

    [TestCase("wrong\n0,0,0\n")]
    [TestCase("frame,p1,p2\n")]
    [TestCase("frame,p1,p2\n1,0,0\n")]
    public void MalformedReplayReturnsCliExitTwo(string content)
    {
        string path = Path.GetTempFileName();
        TextWriter previousError = Console.Error;
        using var errors = new StringWriter();
        try
        {
            File.WriteAllText(path, content);
            Console.SetError(errors);
            int code = Program.Main(new[] { "--replay", path });
            Assert.That(code, Is.EqualTo(2));
            Assert.That(errors.ToString(), Is.Not.Empty);
        }
        finally
        {
            Console.SetError(previousError);
            File.Delete(path);
        }
    }

    [Test]
    public void ReplayCannotSetReservedInputBit()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "frame,p1,p2\n0,0,128\n");
            Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.ReadInputs(path));
        }
        finally { File.Delete(path); }
    }
}
