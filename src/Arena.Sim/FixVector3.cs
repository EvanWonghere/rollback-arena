using System;

namespace Arena.Sim
{
    /// <summary>确定性三维向量。点积按 X、Y、Z 顺序逐项舍入、饱和。</summary>
    public readonly struct FixVector3 : IEquatable<FixVector3>
    {
        public readonly Fixed64 X;
        public readonly Fixed64 Y;
        public readonly Fixed64 Z;

        public FixVector3(Fixed64 x, Fixed64 y, Fixed64 z) { X = x; Y = y; Z = z; }

        public static readonly FixVector3 Zero = default;
        public static readonly FixVector3 UnitX = new FixVector3(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        public static readonly FixVector3 UnitY = new FixVector3(Fixed64.Zero, Fixed64.One, Fixed64.Zero);
        public static readonly FixVector3 UnitZ = new FixVector3(Fixed64.Zero, Fixed64.Zero, Fixed64.One);

        public static Fixed64 Dot(FixVector3 a, FixVector3 b) =>
            (a.X * b.X + a.Y * b.Y) + a.Z * b.Z;

        public Fixed64 LengthSquared => Dot(this, this);

        // 大向量先缩放，避免平方过早饱和；极小向量也不会因平方舍入为零而丢方向。
        Fixed64 Scale => Fixed64.Max(Fixed64.Abs(X), Fixed64.Max(Fixed64.Abs(Y), Fixed64.Abs(Z)));

        public Fixed64 Length
        {
            get
            {
                Fixed64 scale = Scale;
                if (scale == Fixed64.Zero) return Fixed64.Zero;
                FixVector3 scaled = this / scale;
                return scale * Fixed64.Sqrt(Dot(scaled, scaled));
            }
        }

        public FixVector3 Normalized
        {
            get
            {
                Fixed64 scale = Scale;
                if (scale == Fixed64.Zero) return Zero;
                FixVector3 scaled = this / scale;
                return scaled / Fixed64.Sqrt(Dot(scaled, scaled));
            }
        }

        public static FixVector3 operator +(FixVector3 a, FixVector3 b) => new FixVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static FixVector3 operator -(FixVector3 a, FixVector3 b) => new FixVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static FixVector3 operator -(FixVector3 value) => new FixVector3(-value.X, -value.Y, -value.Z);
        public static FixVector3 operator *(FixVector3 value, Fixed64 scalar) => new FixVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        public static FixVector3 operator *(Fixed64 scalar, FixVector3 value) => value * scalar;
        public static FixVector3 operator /(FixVector3 value, Fixed64 scalar) => new FixVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        public static bool operator ==(FixVector3 a, FixVector3 b) => a.Equals(b);
        public static bool operator !=(FixVector3 a, FixVector3 b) => !a.Equals(b);
        public bool Equals(FixVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is FixVector3 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
    }
}
