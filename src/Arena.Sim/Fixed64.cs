using System;
using System.Globalization;
using System.Text;

namespace Arena.Sim
{
    /// <summary>
    /// Q32.32 定点数：内部是一个 long，值 = RawValue / 2^32。
    /// 范围约 ±2.1×10^9，精度 2^-32（约 2.3×10^-10）。
    /// </summary>
    /// <remarks>
    /// 溢出策略：饱和。所有运算的结果都是「精确结果夹到 [MinValue, MaxValue]」，所以 -MinValue == MaxValue。
    /// 乘法的舍入：四舍五入，恰好一半时远离零（见方案 S1）。
    /// </remarks>
    public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
    {
        public const int FractionalBits = 32;

        public static readonly Fixed64 Zero = new Fixed64(0);
        public static readonly Fixed64 One = new Fixed64(1L << FractionalBits);
        public static readonly Fixed64 Half = new Fixed64(1L << (FractionalBits - 1));
        public static readonly Fixed64 Epsilon = new Fixed64(1);
        public static readonly Fixed64 MinValue = new Fixed64(long.MinValue);
        public static readonly Fixed64 MaxValue = new Fixed64(long.MaxValue);

        public readonly long RawValue;

        Fixed64(long rawValue)
        {
            RawValue = rawValue;
        }

        public static Fixed64 FromRaw(long rawValue) => new Fixed64(rawValue);

        // int 左移 32 位一定放得进 long，不会溢出。
        public static Fixed64 FromInt(int value) => new Fixed64((long)value << FractionalBits);

        public static Fixed64 Add(Fixed64 a, Fixed64 b)
        {
            long x = a.RawValue, y = b.RawValue;
            long sum = unchecked(x + y);
            // 两个加数同号、结果却变号，才是溢出；方向跟加数的符号走。
            if (((x ^ sum) & (y ^ sum)) < 0)
                return x < 0 ? MinValue : MaxValue;
            return new Fixed64(sum);
        }

        public static Fixed64 Sub(Fixed64 a, Fixed64 b)
        {
            long x = a.RawValue, y = b.RawValue;
            long diff = unchecked(x - y);
            // 被减数和减数异号、结果和被减数异号，才是溢出；方向跟被减数的符号走。
            if (((x ^ y) & (x ^ diff)) < 0)
                return x < 0 ? MinValue : MaxValue;
            return new Fixed64(diff);
        }

        public static Fixed64 Negate(Fixed64 a) =>
            a.RawValue == long.MinValue ? MaxValue : new Fixed64(-a.RawValue);

        public static Fixed64 Mul(Fixed64 a, Fixed64 b)
        {
            throw new NotImplementedException("由人实现，见方案 S1");
        }

        public static Fixed64 operator +(Fixed64 a, Fixed64 b) => Add(a, b);
        public static Fixed64 operator -(Fixed64 a, Fixed64 b) => Sub(a, b);
        public static Fixed64 operator -(Fixed64 a) => Negate(a);
        public static Fixed64 operator *(Fixed64 a, Fixed64 b) => Mul(a, b);

        public static bool operator ==(Fixed64 a, Fixed64 b) => a.RawValue == b.RawValue;
        public static bool operator !=(Fixed64 a, Fixed64 b) => a.RawValue != b.RawValue;
        public static bool operator <(Fixed64 a, Fixed64 b) => a.RawValue < b.RawValue;
        public static bool operator >(Fixed64 a, Fixed64 b) => a.RawValue > b.RawValue;
        public static bool operator <=(Fixed64 a, Fixed64 b) => a.RawValue <= b.RawValue;
        public static bool operator >=(Fixed64 a, Fixed64 b) => a.RawValue >= b.RawValue;

        public int CompareTo(Fixed64 other) => RawValue.CompareTo(other.RawValue);
        public bool Equals(Fixed64 other) => RawValue == other.RawValue;
        public override bool Equals(object? obj) => obj is Fixed64 other && Equals(other);
        public override int GetHashCode() => RawValue.GetHashCode();

        /// <summary>
        /// 只给调试用：十进制，小数部分向零截断到 10 位并去掉末尾的 0。
        /// 相邻两个值相差 2^-32 &gt; 10^-10，所以不同的值一定打印成不同的字符串。
        /// </summary>
        public override string ToString()
        {
            // 用 ulong 取绝对值，MinValue 也放得下。
            ulong magnitude = RawValue < 0 ? unchecked((ulong)-RawValue) : (ulong)RawValue;
            ulong integerPart = magnitude >> FractionalBits;
            ulong fraction = magnitude & 0xFFFFFFFFUL;

            var text = new StringBuilder();
            if (RawValue < 0)
                text.Append('-');
            text.Append(integerPart.ToString(CultureInfo.InvariantCulture));
            if (fraction == 0)
                return text.ToString();

            text.Append('.');
            for (int i = 0; i < 10 && fraction != 0; i++)
            {
                fraction *= 10; // fraction < 2^32，乘 10 不会溢出 ulong。
                text.Append((char)('0' + (int)(fraction >> FractionalBits)));
                fraction &= 0xFFFFFFFFUL;
            }
            return text.ToString().TrimEnd('0');
        }
    }
}
