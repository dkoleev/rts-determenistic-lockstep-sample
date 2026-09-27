using System;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// Fixed-point number, Q48.16 stored in a long.
    ///
    /// Why not float: IEEE float results can differ between CPUs, compilers (Mono / IL2CPP),
    /// optimization levels (FMA, x87 80-bit registers) and math libraries. In lockstep every
    /// peer must produce bit-identical state, so the simulation uses integers only.
    ///
    /// Range: values up to ~2^23 are safe for multiplication (raw * raw fits in a long).
    /// That is plenty for an RTS map measured in meters.
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public const int FractionalBits = 16;
        public const long OneRaw = 1L << FractionalBits;

        public static readonly Fix64 Zero = new Fix64(0);
        public static readonly Fix64 One = new Fix64(OneRaw);

        public readonly long Raw;

        private Fix64(long raw)
        {
            Raw = raw;
        }

        public static Fix64 FromRaw(long raw) => new Fix64(raw);

        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        /// <summary>Deterministic way to author fractional constants: FromRatio(3, 2) == 1.5.</summary>
        public static Fix64 FromRatio(int numerator, int denominator) =>
            new Fix64(((long)numerator << FractionalBits) / denominator);

        /// <summary>
        /// Input boundary ONLY (e.g. a mouse click turned into a move target).
        /// The conversion happens on a single machine, then the raw long travels over the wire,
        /// so every peer still sees the same value. Never call this inside the simulation.
        /// </summary>
        public static Fix64 FromFloat(float value) => new Fix64((long)Math.Round(value * OneRaw));

        /// <summary>Rendering only. Floats may leave the simulation, never enter it.</summary>
        public float ToFloat() => Raw / (float)OneRaw;

        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.Raw + b.Raw);
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.Raw - b.Raw);
        public static Fix64 operator -(Fix64 a) => new Fix64(-a.Raw);
        public static Fix64 operator *(Fix64 a, Fix64 b) => new Fix64((a.Raw * b.Raw) >> FractionalBits);
        public static Fix64 operator /(Fix64 a, Fix64 b) => new Fix64((a.Raw << FractionalBits) / b.Raw);
        public static Fix64 operator *(Fix64 a, int b) => new Fix64(a.Raw * b);
        public static Fix64 operator /(Fix64 a, int b) => new Fix64(a.Raw / b);

        public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
        public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
        public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
        public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
        public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

        public static Fix64 Abs(Fix64 v) => v.Raw < 0 ? new Fix64(-v.Raw) : v;
        public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;
        public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;

        /// <summary>Integer-only square root (Newton's method), so it is bit-exact everywhere.</summary>
        public static Fix64 Sqrt(Fix64 v)
        {
            if (v.Raw <= 0)
                return Zero;

            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
            return new Fix64((long)ISqrt((ulong)v.Raw << FractionalBits));
        }

        private static ulong ISqrt(ulong n)
        {
            ulong x = n;
            ulong y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }

            return x;
        }

        public bool Equals(Fix64 other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix64 other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToFloat().ToString("0.###");
    }
}
