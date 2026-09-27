using System;
using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>Message layout: [MessageType:byte][payload]. Little-endian, via BinaryWriter.</summary>
    public static class Protocol
    {
        public static byte[] Join() => Build(MessageType.Join, w => { });

        public static byte[] Snapshot(byte playerId, World world) => Build(MessageType.Snapshot, w =>
        {
            w.Write(playerId);
            world.Write(w);
        });

        public static byte[] Command(Command command) => Build(MessageType.Command, command.Write);

        public static byte[] TickFrame(TickFrame frame) => Build(MessageType.TickFrame, frame.Write);

        public static byte[] StateHash(int tick, uint hash) => Build(MessageType.StateHash, w =>
        {
            w.Write(tick);
            w.Write(hash);
        });

        /// <summary>Returns a reader positioned right after the message type.</summary>
        public static BinaryReader Open(byte[] data, out MessageType type)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            type = (MessageType)reader.ReadByte();
            return reader;
        }

        private static byte[] Build(MessageType type, Action<BinaryWriter> writeBody)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            writer.Write((byte)type);
            writeBody(writer);
            writer.Flush();
            
            return stream.ToArray();
        }
    }
}
