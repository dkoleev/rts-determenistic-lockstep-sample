using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rts.Lockstep.Code.Core;

namespace Rts.Lockstep.Demo.Code.Tests
{
    public class LockstepTests
    {
        [Test]
        public void Fix64_BasicMath()
        {
            Assert.AreEqual(Fix64.FromInt(2), Fix64.Sqrt(Fix64.FromInt(4)));
            Assert.AreEqual(Fix64.FromRatio(3, 2), Fix64.FromInt(3) / Fix64.FromInt(2));
            Assert.AreEqual(Fix64.FromRatio(-3, 4), Fix64.FromRatio(3, 2) * Fix64.FromRatio(-1, 2));
            Assert.AreEqual(5f, new FixVec2(Fix64.FromInt(3), Fix64.FromInt(4)).Magnitude.ToFloat(), 0.001f);
        }

        [Test]
        public void SameCommands_ProduceIdenticalWorlds()
        {
            var a = new World();
            var b = new World();
            var frames = RandomFrames(seed: 42, ticks: 600);

            foreach (var frame in frames)
            {
                Simulation.Step(a, frame);
                Simulation.Step(b, frame);
                Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), $"tick {a.Tick}");
            }
        }

        [Test]
        public void Snapshot_RoundTrip_KeepsHash()
        {
            var world = new World();
            foreach (List<Command> frame in RandomFrames(seed: 7, ticks: 200))
                Simulation.Step(world, frame);

            var stream = new MemoryStream();
            world.Write(new BinaryWriter(stream));
            stream.Position = 0;
            World copy = World.Read(new BinaryReader(stream));

            Assert.AreEqual(world.ComputeHash(), copy.ComputeHash());
        }

        [Test]
        public void Command_ForSomeoneElsesUnits_IsIgnored()
        {
            var world = new World();
            Simulation.Step(world, new[] { Command.SpawnPlayer(1), Command.SpawnPlayer(2) });

            Unit enemy = world.Units.Find(u => u.Owner == 2);
            FixVec2 before = enemy.Position;

            Command hijack = Command.Move(new[] { enemy.Id }, FixVec2.FromInt(0, 0));
            hijack.PlayerId = 1;
            Simulation.Step(world, new[] { hijack });

            Assert.IsFalse(enemy.IsMoving);
            Assert.AreEqual(before.X, enemy.Position.X);
        }

        [Test]
        public void ServerAndClients_StayInSync_WithLatencyJitterAndLateJoin()
        {
            var net = new SimulatedNetwork();
            var server = new LockstepServer();
            var fast = Connect(net, server, new LinkSettings(30, 10), seed: 1);
            var slow = Connect(net, server, new LinkSettings(180, 60), seed: 2);
            var random = new Random(3);

            fast.Join();
            for (int frame = 0; frame < 60 * 30; frame++) // 30 seconds at ~60 fps
            {
                if (frame == 60 * 3)
                    slow.Join(); // late join through a snapshot

                IssueRandomOrder(fast, random);
                IssueRandomOrder(slow, random);

                var dt = 1.0 / 60 + random.NextDouble() * 0.01; // uneven frame times
                net.Advance(dt);
                server.Update(dt);
                fast.Update(dt);
                slow.Update(dt);
            }

            Assert.AreEqual(0, server.DesyncCount);
            Assert.Greater(fast.World.Tick, 500);
            Assert.Greater(slow.World.Tick, 500);
            Assert.AreEqual(2 * Simulation.UnitsPerPlayer, slow.World.Units.Count);
        }

        [Test]
        public void Desync_IsDetected_AndFixedBySnapshot()
        {
            var net = new SimulatedNetwork();
            var server = new LockstepServer();
            var client = Connect(net, server, new LinkSettings(50, 0), seed: 1);
            client.Join();

            Run(net, server, client, seconds: 2);
            client.CorruptStateForDemo();
            Run(net, server, client, seconds: 2);
            Assert.AreEqual(1, server.DesyncCount);

            Run(net, server, client, seconds: 5);
            Assert.AreEqual(1, server.DesyncCount, "client must be healthy after the resync snapshot");
        }

        private static LockstepClient Connect(SimulatedNetwork net, LockstepServer server, LinkSettings link, int seed)
        {
            net.Connect(link, seed, out IChannel clientSide, out IChannel serverSide);
            server.AddConnection(serverSide);
            return new LockstepClient(clientSide);
        }

        private static void Run(SimulatedNetwork net, LockstepServer server, LockstepClient client, double seconds)
        {
            const double dt = 1.0 / 60;
            for (double t = 0; t < seconds; t += dt)
            {
                net.Advance(dt);
                server.Update(dt);
                client.Update(dt);
            }
        }

        private static void IssueRandomOrder(LockstepClient client, Random random)
        {
            if (!client.IsJoined || random.Next(20) != 0)
                return;

            var mine = client.World.Units.FindAll(u => u.Owner == client.PlayerId).ConvertAll(u => u.Id).ToArray();
            client.SendCommand(random.Next(5) == 0
                ? Command.Stop(mine)
                : Command.Move(mine, FixVec2.FromInt(random.Next(-20, 20), random.Next(-10, 10))));
        }

        private static List<Command>[] RandomFrames(int seed, int ticks)
        {
            var random = new Random(seed);
            var frames = new List<Command>[ticks];
            for (var i = 0; i < ticks; i++)
            {
                frames[i] = new List<Command>();
                if (i == 0)
                {
                    frames[i].Add(Command.SpawnPlayer(1));
                    frames[i].Add(Command.SpawnPlayer(2));
                }
                else if (random.Next(10) == 0)
                {
                    var move = Command.Move(new[] { random.Next(1, 13), random.Next(1, 13) },
                        new FixVec2(Fix64.FromRatio(random.Next(-2000, 2000), 100), Fix64.FromInt(random.Next(-10, 10))));
                    move.PlayerId = (byte)random.Next(1, 3);
                    frames[i].Add(move);
                }
            }

            return frames;
        }
    }
}
