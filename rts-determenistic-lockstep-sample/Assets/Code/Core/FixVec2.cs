namespace Rts.Lockstep.Code.Core
{
    /// <summary>2D fixed-point vector. The RTS map is a plane (X, Y), the view maps it to Unity's X/Z.</summary>
    public readonly struct FixVec2
    {
        public static readonly FixVec2 Zero = new FixVec2(Fix64.Zero, Fix64.Zero);

        public readonly Fix64 X;
        public readonly Fix64 Y;

        public FixVec2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        public static FixVec2 FromInt(int x, int y) => new FixVec2(Fix64.FromInt(x), Fix64.FromInt(y));

        public Fix64 SqrMagnitude => X * X + Y * Y;
        public Fix64 Magnitude => Fix64.Sqrt(SqrMagnitude);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator *(FixVec2 a, Fix64 s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix64 s) => new FixVec2(a.X / s, a.Y / s);

        public override string ToString() => $"({X}, {Y})";

    }
}
