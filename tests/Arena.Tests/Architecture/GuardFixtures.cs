using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

// 故意写坏的代码，用来证明 DeterminismScanner 真的能抓到每一类问题（守卫自己的反向探针）。
// 这些类型只在测试程序集里，不会进入 Arena.Sim / Arena.Rollback。
namespace Arena.Tests.Architecture.Fixtures;

public struct FloatField { public float X; }

public static class DoubleLocal
{
    public static long M(long x) { double d = x; d *= 3; return (long)d; }
}

public static class MathSqrt
{
    public static int M(int x) => (int)Math.Sqrt(x);
}

public static class FloatInLambda
{
    public static Func<int> M(int x) => () => (int)(x * 1.5f);
}

public static class RandomUse
{
    public static int M() => new Random(1).Next();
}

public static class DateTimeUse
{
    public static long M() => DateTime.Now.Ticks;
}

public static class StopwatchUse
{
    public static long M() => Stopwatch.GetTimestamp();
}

public static class TickCountUse
{
    public static int M() => Environment.TickCount;
}

public static class DictionaryForeach
{
    public static int M(Dictionary<int, int> d)
    {
        int sum = 0;
        foreach (var kv in d) sum = sum * 31 + kv.Value;
        return sum;
    }
}

public static class DictionaryKeys
{
    public static int[] M(Dictionary<int, int> d) => d.Keys.ToArray();
}

public static class DictionaryValuesForeach
{
    public static int M(Dictionary<int, int> d)
    {
        int sum = 0;
        foreach (var v in d.Values) sum = sum * 31 + v;
        return sum;
    }
}

public static class HashSetForeach
{
    public static int M(HashSet<int> s)
    {
        int sum = 0;
        foreach (var v in s) sum = sum * 31 + v;
        return sum;
    }
}

public struct NumericsVector
{
    public Vector2 Pos, Vel;
    public void Step() { Pos += Vel; Vel = Vector2.Normalize(Vel); }
}

public static class CryptoRandomUse
{
    public static int M() => RandomNumberGenerator.GetInt32(6);
}

public static class HashCodeUse
{
    public static int M(int a, int b) => HashCode.Combine(a, b);
}

public static class GuidUse
{
    public static Guid M() => Guid.NewGuid();
}

public static class HashtableUse
{
    public static int M(Hashtable t)
    {
        int n = 0;
        foreach (var _ in t) n++;
        return n;
    }
}

public static class ConcurrentDictionaryUse
{
    public static int M(ConcurrentDictionary<int, int> d) => d.Keys.Count;
}

public static class HashSetCopyTo
{
    public static int[] M(HashSet<int> s) { var a = new int[s.Count]; s.CopyTo(a); return a; }
}

public static class HashSetRemoveWhere
{
    public static int M(HashSet<int> s, List<int> log) => s.RemoveWhere(x => { log.Add(x); return x > 3; });
}

public static class ReadOnlyDictionaryKeys
{
    public static int M(IReadOnlyDictionary<int, int> d)
    {
        int sum = 0;
        foreach (var k in d.Keys) sum = sum * 31 + k;
        return sum;
    }
}

/// <summary>允许的写法：整数运算、数组、List 遍历、Dictionary 只查不遍历。不应报任何问题。</summary>
public static class Clean
{
    public static long M(int[] xs, List<int> list, Dictionary<int, int> d)
    {
        long sum = 0;
        foreach (var x in xs) sum += x;
        foreach (var x in list) sum = sum * 31 + x;
        if (d.TryGetValue(1, out var v)) sum += v;
        return sum + d.Count + Math.Abs(-3) + (sum >> 2);
    }
}
