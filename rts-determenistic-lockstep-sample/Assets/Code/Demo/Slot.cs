using Rts.Lockstep.Code.Core;

namespace Rts.Lockstep.Demo.Code.Demo
{
    public class Slot
    {
        public string Name;
        public LinkSettings Link;
        public IChannel ServerSide;
        public LockstepClient Client;
        public ClientView View;
        public long LastBytes;
        public float BytesPerSecond;
    }
}