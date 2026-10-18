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
            const ulong Mask = 0xFFFFFFFFUL;

            bool isNegative = (a.RawValue < 0) ^ (b.RawValue < 0);

            // Safe unsigned absolute values
            ulong x = a.RawValue < 0
                ? unchecked(0UL - (ulong)a.RawValue)
                : (ulong)a.RawValue;

            ulong y = b.RawValue < 0
                ? unchecked(0UL - (ulong)b.RawValue)
                : (ulong)b.RawValue;

            // Split into 32-bit parts
            ulong al = x & Mask;
            ulong ah = x >> 32;
            ulong bl = y & Mask;
            ulong bh = y >> 32;

            // Partial products
            ulong p00 = al * bl;
            ulong p01 = al * bh;
            ulong p10 = ah * bl;
            ulong p11 = ah * bh;

            // Combine and discard the lowest 32 bits
            ulong s = (p00 >> 32)
                    + (p01 & Mask)
                    + (p10 & Mask);

            if ((p00 & 0xFFFFFFFFUL) >= 0x80000000UL)
                s++;

            ulong L = s & Mask;

            ulong H = p11
                    + (p01 >> 32)
                    + (p10 >> 32)
                    + (s >> 32);

            // Signed 64-bit saturation
            ulong limit = isNegative
                ? 0x8000000000000000UL
                : 0x7FFFFFFFFFFFFFFFUL;

            if (H > (limit >> 32) ||
                (H == (limit >> 32) && L > (limit & Mask)))
            {
                return isNegative ? MinValue : MaxValue;
            }

            // Reconstruct signed raw value
            ulong magnitude = (H << 32) | L;

            long raw = isNegative
                ? unchecked((long)(0UL - magnitude))
                : (long)magnitude;

            return new Fixed64(raw);
        }

        /// <summary>最近舍入，半途远离零；溢出饱和。除零抛异常。</summary>
        public static Fixed64 Div(Fixed64 a, Fixed64 b)
        {
            if (b.RawValue == 0)
                throw new DivideByZeroException();

            bool negative = (a.RawValue < 0) ^ (b.RawValue < 0);
            ulong numerator = UnsignedMagnitude(a.RawValue);
            ulong denominator = UnsignedMagnitude(b.RawValue);
            ulong limit = negative ? 0x8000000000000000UL : 0x7FFFFFFFFFFFFFFFUL;
            ulong integer = numerator / denominator;
            if (integer > (limit >> FractionalBits))
                return negative ? MinValue : MaxValue;

            // 先处理整数部分，再逐位求 32 位小数，避免构造 96 位分子。
            // remainder < denominator <= 2^63，因此左移一位不会丢失有效位。
            ulong quotient = integer << FractionalBits;
            ulong remainder = numerator % denominator;
            for (int bit = FractionalBits - 1; bit >= 0; bit--)
            {
                remainder <<= 1;
                if (remainder >= denominator)
                {
                    remainder -= denominator;
                    quotient |= 1UL << bit;
                }
            }

            // 等价于 2 * remainder >= denominator，不使余数乘二溢出。
            if (remainder >= (denominator + 1) / 2)
                quotient++;
            if (quotient > limit)
                return negative ? MinValue : MaxValue;
            return new Fixed64(negative ? unchecked((long)(0UL - quotient)) : (long)quotient);
        }

        /// <summary>非负输入的最近定点平方根；负输入抛异常。</summary>
        public static Fixed64 Sqrt(Fixed64 value)
        {
            if (value.RawValue < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Square root requires a non-negative value.");

            // 对概念上的 96 位数 raw << 32 逐两位开方，不实际构造宽整数。
            // root < 2^48，余数和试减项也远小于 2^64。
            ulong raw = (ulong)value.RawValue;
            ulong root = 0;
            ulong remainder = 0;
            for (int pair = 47; pair >= 0; pair--)
            {
                ulong digit = pair >= 16 ? (raw >> (pair * 2 - 32)) & 3UL : 0;
                remainder = (remainder << 2) | digit;
                root <<= 1;
                ulong trial = (root << 1) | 1UL;
                if (remainder >= trial)
                {
                    remainder -= trial;
                    root++;
                }
            }

            // N - root^2 > root 等价于 sqrt(N) > root + 0.5；N 为整数，不会恰好半途。
            if (remainder > root)
                root++;
            return new Fixed64((long)root);
        }

        public static Fixed64 Abs(Fixed64 value) => value.RawValue < 0 ? Negate(value) : value;
        public static Fixed64 Min(Fixed64 a, Fixed64 b) => a.RawValue <= b.RawValue ? a : b;
        public static Fixed64 Max(Fixed64 a, Fixed64 b) => a.RawValue >= b.RawValue ? a : b;

        static ulong UnsignedMagnitude(long raw) => raw < 0 ? unchecked(0UL - (ulong)raw) : (ulong)raw;

        public static Fixed64 operator +(Fixed64 a, Fixed64 b) => Add(a, b);
        public static Fixed64 operator -(Fixed64 a, Fixed64 b) => Sub(a, b);
        public static Fixed64 operator -(Fixed64 a) => Negate(a);
        public static Fixed64 operator *(Fixed64 a, Fixed64 b) => Mul(a, b);
        public static Fixed64 operator /(Fixed64 a, Fixed64 b) => Div(a, b);

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
