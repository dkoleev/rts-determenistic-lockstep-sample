using System;
using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// A player's order. Lockstep sends ORDERS, not state: "units 3,4,5 go to (10, 2)" is a few bytes,
    /// no matter how many units the order affects or how long they walk.
    /// </summary>
    public class Command
    {
        public const int MaxUnitsPerCommand = 64;
        public CommandType Type;
        /// <summary>Who issued the order. Set by the SERVER from the connection, never trusted from the client.</summary>
        public byte PlayerId;
        public int[] UnitIds = Array.Empty<int>();
        public FixVec2 Target;

        public static Command SpawnPlayer(byte playerId) => new() { Type = CommandType.SpawnPlayer, PlayerId = playerId };
        public static Command Move(int[] unitIds, FixVec2 target) => new() { Type = CommandType.Move, UnitIds = unitIds, Target = target };
        public static Command Stop(int[] unitIds) => new() { Type = CommandType.Stop, UnitIds = unitIds };

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Type);
            w.Write(PlayerId);
            w.Write((byte)UnitIds.Length);
            foreach (int id in UnitIds)
                w.Write(id);
            w.Write(Target.X.Raw);
            w.Write(Target.Y.Raw);
        }

        public static Command Read(BinaryReader r)
        {
            var command = new Command
            {
                Type = (CommandType)r.ReadByte(),
                PlayerId = r.ReadByte(),
            };

            int count = r.ReadByte();
            if (count > MaxUnitsPerCommand)
                throw new InvalidDataException($"Too many units in a command: {count}");

            command.UnitIds = new int[count];
            for (int i = 0; i < count; i++)
                command.UnitIds[i] = r.ReadInt32();

            command.Target = new FixVec2(Fix64.FromRaw(r.ReadInt64()), Fix64.FromRaw(r.ReadInt64()));
            return command;
        }

        public override string ToString() =>
            $"{Type} p{PlayerId} [{string.Join(",", UnitIds)}] -> {Target}";
    }
}
