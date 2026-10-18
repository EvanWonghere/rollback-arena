using System;
using System.Collections.Generic;
using System.Reflection;
using Arena.Sim;
using Arena.Tests.FixedMath;
using NUnit.Framework;

namespace Arena.Tests.Simulation;

public class SimulationTests
{
    static FrameInputs[] Inputs(ulong seed, int count)
    {
        var generator = new Fixed64CaseGenerator(seed);
        var inputs = new FrameInputs[count];
        for (int i = 0; i < count; i++)
            inputs[i] = new FrameInputs((PlayerInput)(generator.NextUInt64() & 127), (PlayerInput)(generator.NextUInt64() & 127));
        return inputs;
    }

    [TestCase(0x1234UL)]
    [TestCase(0xFEDCBAUL)]
    public void V2RepeatedInputReplaysMatchEveryFrame(ulong seed)
    {
        var inputs = Inputs(seed, 8000);
        var hashes = new ulong[inputs.Length];
        for (int repetition = 0; repetition < 6; repetition++)
        {
            var state = ArenaGame.Create();
            for (int i = 0; i < inputs.Length; i++)
            {
                ArenaGame.Step(ref state, inputs[i]);
                ulong hash = StateChecksum.Compute(state);
                if (repetition == 0) hashes[i] = hash;
                else if (hash != hashes[i]) Assert.Fail($"seed={seed:X}, repetition={repetition}, frame={i}: {hash:X16} != {hashes[i]:X16}");
            }
        }
        TestContext.Out.WriteLine($"V2 seed={seed:X}: 6 replays x 8000 frames match");
    }

    [Test]
    public void InputChangesAreVisibleAtTheAffectedFrame()
    {
        var first = ArenaGame.Create();
        var second = first;
        for (int i = 0; i < 90; i++)
        {
            ArenaGame.Step(ref first, default);
            ArenaGame.Step(ref second, default);
        }
        Assert.That(StateChecksum.Compute(first), Is.EqualTo(StateChecksum.Compute(second)));
        ArenaGame.Step(ref first, default);
        ArenaGame.Step(ref second, new FrameInputs(PlayerInput.Right, PlayerInput.None));
        Assert.That(StateChecksum.Compute(first), Is.Not.EqualTo(StateChecksum.Compute(second)));
    }

    [Test]
    public void SnapshotIsolationAndRestoreThenContinueMatch()
    {
        var inputs = Inputs(0x5A9UL, 1000);
        var original = ArenaGame.Create();
        for (int i = 0; i < 200; i++) ArenaGame.Step(ref original, inputs[i]);
        var captured = original;
        var snapshot = new GameSnapshot(original);
        original.Player1.X = Fixed64.MaxValue;
        original.Player2.PreviousInput = PlayerInput.Attack;
        original.Frame = 123456;
        snapshot.Restore(ref original);
        Assert.That(original, Is.EqualTo(captured));
        for (int i = 200; i < inputs.Length; i++)
        {
            ArenaGame.Step(ref original, inputs[i]);
            ArenaGame.Step(ref captured, inputs[i]);
            Assert.That(StateChecksum.Compute(original), Is.EqualTo(StateChecksum.Compute(captured)), $"frame {i}");
        }
        Assert.That(original, Is.EqualTo(captured));
    }

    public static IEnumerable<TestCaseData> StateMutations()
    {
        var state = ArenaGame.Create();
        foreach (var (name, changed) in MutateLeaves(state, typeof(GameState), "state"))
            yield return new TestCaseData(name, (GameState)changed).SetArgDisplayNames(name);
    }

    static IEnumerable<(string Name, object Value)> MutateLeaves(object value, Type type, string path)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object old = field.GetValue(value)!;
            IEnumerable<(string Name, object Value)> changes;
            if (field.FieldType == typeof(Fixed64)) changes = new[] { (path + "." + field.Name, (object)Fixed64.FromRaw(((Fixed64)old).RawValue + 1)) };
            else if (field.FieldType.IsEnum) changes = new[] { (path + "." + field.Name, Enum.ToObject(field.FieldType, Convert.ToInt32(old) ^ 1)) };
            else if (field.FieldType == typeof(bool)) changes = new[] { (path + "." + field.Name, (object)!(bool)old) };
            else if (field.FieldType.IsPrimitive) changes = new[] { (path + "." + field.Name, Convert.ChangeType(Convert.ToInt64(old) + 1, field.FieldType)) };
            else changes = MutateLeaves(old, field.FieldType, path + "." + field.Name);
            foreach (var (name, changed) in changes)
            {
                object copy = Activator.CreateInstance(type)!;
                foreach (FieldInfo member in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) member.SetValue(copy, member.GetValue(value));
                field.SetValue(copy, changed);
                yield return (name, copy);
            }
        }
    }

    [TestCaseSource(nameof(StateMutations))]
    public void EveryStateLeafChangesChecksum(string field, GameState changed)
    {
        var original = ArenaGame.Create();
        Assert.That(StateChecksum.Compute(changed), Is.Not.EqualTo(StateChecksum.Compute(original)), field);
    }

    [TestCaseSource(nameof(StateMutations))]
    public void SnapshotCapturesEveryStateLeaf(string field, GameState changed)
    {
        var snapshot = new GameSnapshot(changed);
        var restored = ArenaGame.Create();
        snapshot.Restore(ref restored);
        Assert.That(restored, Is.EqualTo(changed), field);
    }

    [TestCaseSource(nameof(StateMutations))]
    public void SnapshotRestoresEveryStateLeaf(string field, GameState changed)
    {
        var original = ArenaGame.Create();
        var snapshot = new GameSnapshot(original);
        snapshot.Restore(ref changed);
        Assert.That(changed, Is.EqualTo(original), field);
    }

    [Test]
    public void InitialChecksumMatchesIndependentLittleEndianGoldenBytes()
    {
        // Python struct.pack independently encoded the 189 canonical bytes, then FNV-1a.
        var state = ArenaGame.Create();
        Assert.That(StateChecksum.Compute(state), Is.EqualTo(0xAC32AB911BBFFD1DUL));
    }

    [Test]
    public void StateAndSnapshotContainNoReferences()
    {
        Check(typeof(GameState));
        Check(typeof(GameSnapshot));
        static void Check(Type type)
        {
            Assert.That(type.IsValueType, Is.True, type.FullName);
            if (type.IsPrimitive || type.IsEnum) return;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) Check(field.FieldType);
        }
        Assert.That(System.Linq.Enumerable.Count(StateMutations()), Is.EqualTo(45), "update canonical layout intentionally if state changes");
    }
}
