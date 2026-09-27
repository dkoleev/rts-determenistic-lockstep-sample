namespace Rts.Lockstep.Code.Core
{
    public enum CommandType : byte
    {
        SpawnPlayer = 1, // issued by the server only, when a player joins
        Move = 2,
        Stop = 3,
    }
}
