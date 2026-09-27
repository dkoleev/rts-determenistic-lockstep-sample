namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// One end of a reliable, ordered connection (think TCP, or a reliable UDP channel).
    /// Lockstep needs reliability: losing a single frame would stall or desync the game.
    /// </summary>
    public interface IChannel
    {
        long BytesSent { get; }
        void Send(byte[] data);
        bool TryReceive(out byte[] data);
    }
}