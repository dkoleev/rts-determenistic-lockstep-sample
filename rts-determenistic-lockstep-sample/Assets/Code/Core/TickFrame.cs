using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// The heartbeat of lockstep. The server seals one frame per tick, even an empty one:
    /// a client may simulate tick N only after it has received frame N.
    /// </summary>
    public class TickFrame
    {
        public int Tick;
        public Command[] Commands;

        public TickFrame(int tick, Command[] commands)
        {
            Tick = tick;
            Commands = commands;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Tick);
            w.Write((ushort)Commands.Length);
            foreach (Command command in Commands)
                command.Write(w);
        }

        public static TickFrame Read(BinaryReader r)
        {
            int tick = r.ReadInt32();
            var commands = new Command[r.ReadUInt16()];
            for (int i = 0; i < commands.Length; i++)
                commands[i] = Command.Read(r);
            return new TickFrame(tick, commands);
        }
    }
}
