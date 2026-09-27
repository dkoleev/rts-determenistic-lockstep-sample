namespace Rts.Lockstep.Code.Core
{
    public class Unit
    {
        public int Id;
        public byte Owner;
        public FixVec2 Position;
        public FixVec2 Target;
        public bool IsMoving;
    }
}
