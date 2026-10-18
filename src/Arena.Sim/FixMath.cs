namespace Arena.Sim
{
    /// <summary>弧度三角函数。[-1024,1024] 上绝对误差不超过 1e-6；详见 math-contracts.md。</summary>
    public static partial class FixMath
    {
        const long Period = 26986075409L;
        const int Intervals = 4096;

        public static readonly Fixed64 Pi = Fixed64.FromRaw(13493037705L);
        public static readonly Fixed64 TwoPi = Fixed64.FromRaw(Period);
        public static readonly Fixed64 HalfPi = Fixed64.FromRaw(6746518852L);

        public static Fixed64 Sin(Fixed64 radians) => Sample(radians.RawValue, 0);
        public static Fixed64 Cos(Fixed64 radians) => Sample(radians.RawValue, Intervals / 4);

        static Fixed64 Sample(long raw, int shift)
        {
            long angle = raw % Period;
            if (angle < 0) angle += Period;
            // angle < Period，乘 4096 后小于 2^47。
            long position = angle * Intervals;
            int index = ((int)(position / Period) + shift) & (Intervals - 1);
            long remainder = position % Period;
            long first = SinTable[index];
            long delta = SinTable[index + 1] - first;
            // 相邻样本差 < 6,600,000；乘 remainder 的幅值 < 1.8e17，long 可容纳。
            long product = delta * remainder;
            long offset = ((product < 0 ? -product : product) + Period / 2) / Period;
            return Fixed64.FromRaw(first + (product < 0 ? -offset : offset));
        }
    }
}
