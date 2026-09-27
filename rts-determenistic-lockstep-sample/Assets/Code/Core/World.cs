using System.Collections.Generic;
using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// The whole simulation state. Rules that keep it deterministic:
    ///  - only integer / fixed-point data, no float, no DateTime, no UnityEngine.Random;
    ///  - collections iterated in a defined order (List sorted by Id, never Dictionary / HashSet order);
    ///  - no references to anything outside of the World (views, input, network).
    /// </summary>
    public class World
    {
        public int Tick;
        public int NextUnitId = 1;

        /// <summary>Always sorted by Id: ids only grow and units are appended.</summary>
        public readonly List<Unit> Units = new();

        public Unit SpawnUnit(byte owner, FixVec2 position)
        {
            var unit = new Unit { Id = NextUnitId++, Owner = owner, Position = position, Target = position };
            Units.Add(unit);
            return unit;
        }

        public Unit FindUnit(int id)
        {
            int lo = 0, hi = Units.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int midId = Units[mid].Id;
                if (midId == id) return Units[mid];
                if (midId < id) lo = mid + 1;
                else hi = mid - 1;
            }

            return null;
        }

        /// <summary>
        /// FNV-1a over every field of the state. Peers exchange this number every few ticks:
        /// if it differs, somebody desynced (a non-deterministic bug or a cheater).
        /// </summary>
        public uint ComputeHash()
        {
            uint h = 2166136261;
            Mix(ref h, Tick);
            Mix(ref h, NextUnitId);
            foreach (Unit u in Units)
            {
                Mix(ref h, u.Id);
                Mix(ref h, u.Owner);
                Mix(ref h, u.Position.X.Raw);
                Mix(ref h, u.Position.Y.Raw);
                Mix(ref h, u.Target.X.Raw);
                Mix(ref h, u.Target.Y.Raw);
                Mix(ref h, u.IsMoving ? 1 : 0);
            }

            return h;
        }

        private static void Mix(ref uint h, long value)
        {
            Mix(ref h, (int)value);
            Mix(ref h, (int)(value >> 32));
        }

        private static void Mix(ref uint h, int value)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (byte)(value >> (i * 8));
                h *= 16777619;
            }
        }

        /// <summary>Full snapshot, used for late join and for resync after a desync.</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(Tick);
            w.Write(NextUnitId);
            w.Write(Units.Count);
            foreach (var u in Units)
            {
                w.Write(u.Id);
                w.Write(u.Owner);
                w.Write(u.Position.X.Raw);
                w.Write(u.Position.Y.Raw);
                w.Write(u.Target.X.Raw);
                w.Write(u.Target.Y.Raw);
                w.Write(u.IsMoving);
            }
        }

        public static World Read(BinaryReader r)
        {
            var world = new World
            {
                Tick = r.ReadInt32(),
                NextUnitId = r.ReadInt32(),
            };

            var count = r.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                world.Units.Add(new Unit
                {
                    Id = r.ReadInt32(),
                    Owner = r.ReadByte(),
                    Position = new FixVec2(Fix64.FromRaw(r.ReadInt64()), Fix64.FromRaw(r.ReadInt64())),
                    Target = new FixVec2(Fix64.FromRaw(r.ReadInt64()), Fix64.FromRaw(r.ReadInt64())),
                    IsMoving = r.ReadBoolean(),
                });
            }

            return world;
        }

    }
}
