namespace Rts.Lockstep.Code.Core
{
    public enum MessageType : byte
    {
        Join = 1,      // client -> server: "let me in"
        Snapshot = 2,  // server -> client: your player id + full world (late join / resync)
        Command = 3,   // client -> server: a player order
        TickFrame = 4, // server -> clients: all orders to execute at tick N
        StateHash = 5, // client -> server: "my world hash after tick N is X"
    }
}
