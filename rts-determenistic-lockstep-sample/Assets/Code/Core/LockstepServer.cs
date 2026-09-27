using System;
using System.Collections.Generic;
using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// Authoritative lockstep server.
    ///  - Owns the clock: seals one TickFrame per tick and broadcasts it (even when it's empty).
    ///  - Owns the order: commands are executed in the order the server received them.
    ///  - Owns identity: stamps PlayerId from the connection, clients can't impersonate each other.
    ///  - Runs the same Simulation itself: can hand out snapshots (late join) and verify client hashes.
    /// It never sends unit positions during normal play, only orders.
    /// </summary>
    public class LockstepServer
    {
        public event Action<string> Log;

        public World World => _world;
        public int DesyncCount { get; private set; }
        private const int HashHistoryTicks = 256;

        private class Peer
        {
            public IChannel Channel;
            public byte PlayerId;
            public bool Joined;
            public int ResyncTick = -1; // hashes up to this tick were in flight before the resync, ignore them
        }

        private readonly World _world = new();
        private readonly List<Peer> _peers = new();
        private readonly List<Command> _pendingCommands = new();
        private readonly Dictionary<int, uint> _hashHistory = new();
        private double _accumulator;
        private byte _nextPlayerId = 1;

        /// <summary>Accept a new connection (a socket accept() in a real game).</summary>
        public void AddConnection(IChannel channel) => _peers.Add(new Peer { Channel = channel });

        public void Update(double deltaTime)
        {
            foreach (var peer in _peers)
                while (peer.Channel.TryReceive(out var data))
                    Handle(peer, data);

            // Fixed timestep: the simulation always advances in whole ticks, whatever the frame rate is.
            _accumulator += deltaTime;
            while (_accumulator >= Simulation.TickDuration)
            {
                _accumulator -= Simulation.TickDuration;
                StepTick();
            }
        }

        private void StepTick()
        {
            // Seal the frame: every order received so far executes at this tick, on every peer.
            var frame = new TickFrame(_world.Tick, _pendingCommands.ToArray());
            _pendingCommands.Clear();

            var packet = Protocol.TickFrame(frame);
            foreach (var peer in _peers)
                if (peer.Joined)
                    peer.Channel.Send(packet);

            Simulation.Step(_world, frame.Commands);

            _hashHistory[_world.Tick] = _world.ComputeHash();
            _hashHistory.Remove(_world.Tick - HashHistoryTicks);
        }

        private void Handle(Peer peer, byte[] data)
        {
            using (BinaryReader reader = Protocol.Open(data, out MessageType type))
            {
                switch (type)
                {
                    case MessageType.Join:
                        if (peer.Joined)
                            return;

                        peer.Joined = true;
                        peer.PlayerId = _nextPlayerId++;

                        // Snapshot of tick N goes out BEFORE frame N on the same ordered channel,
                        // so the new client continues exactly where the snapshot ends.
                        peer.Channel.Send(Protocol.Snapshot(peer.PlayerId, _world));

                        // Even spawning is an order in the stream, so every peer spawns at the same tick.
                        _pendingCommands.Add(Command.SpawnPlayer(peer.PlayerId));
                        Log?.Invoke($"[Server] player {peer.PlayerId} joined at tick {_world.Tick}");
                        break;

                    case MessageType.Command:
                        if (!peer.Joined)
                            return;

                        var command = Command.Read(reader);
                        if (command.Type == CommandType.SpawnPlayer)
                            return; // server-only order

                        command.PlayerId = peer.PlayerId; // never trust the id sent by the client
                        _pendingCommands.Add(command);
                        break;

                    case MessageType.StateHash:
                        CheckHash(peer, reader.ReadInt32(), reader.ReadUInt32());
                        break;
                }
            }
        }

        private void CheckHash(Peer peer, int tick, uint clientHash)
        {
            if (tick <= peer.ResyncTick || !_hashHistory.TryGetValue(tick, out var serverHash))
                return;

            if (clientHash == serverHash)
                return;

            DesyncCount++;
            Log?.Invoke($"[Server] DESYNC player {peer.PlayerId} at tick {tick}: " +
                        $"client {clientHash:X8} != server {serverHash:X8}. Sending snapshot.");

            // The server is the authority: its world wins, the client reloads it.
            peer.ResyncTick = _world.Tick;
            peer.Channel.Send(Protocol.Snapshot(peer.PlayerId, _world));
        }

    }
}
