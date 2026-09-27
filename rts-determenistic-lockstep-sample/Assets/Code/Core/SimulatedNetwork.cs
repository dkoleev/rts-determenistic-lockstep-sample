using System;
using System.Collections.Generic;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// In-process fake network with latency and jitter, so server and clients can live in one Unity scene.
    /// Swap it for sockets (or Fusion / Netcode transport) without touching the lockstep code.
    /// </summary>
    public class SimulatedNetwork
    {
        public double Time { get; private set; }

        public void Advance(double deltaTime) => Time += deltaTime;

        public void Connect(LinkSettings settings, int seed, out IChannel clientSide, out IChannel serverSide)
        {
            var up = new Pipe(this, settings, seed);
            var down = new Pipe(this, settings, seed + 1);
            clientSide = new Endpoint(up, down);
            serverSide = new Endpoint(down, up);
        }

        private class Pipe
        {
            private readonly SimulatedNetwork _network;
            private readonly LinkSettings _settings;
            private readonly Random _random; // jitter only; the network is not part of the simulation
            private readonly Queue<(double deliverAt, byte[] data)> _inFlight = new Queue<(double, byte[])>();
            private double _lastDeliverAt;

            public long BytesSent;

            public Pipe(SimulatedNetwork network, LinkSettings settings, int seed)
            {
                _network = network;
                _settings = settings;
                _random = new Random(seed);
            }

            public void Enqueue(byte[] data)
            {
                var delay = (_settings.LatencyMs + _random.Next(0, _settings.JitterMs + 1)) / 1000.0;
                // Ordered delivery: a packet never overtakes the previous one (like TCP head-of-line blocking).
                var deliverAt = Math.Max(_network.Time + delay, _lastDeliverAt);
                _lastDeliverAt = deliverAt;
                _inFlight.Enqueue((deliverAt, data));
                BytesSent += data.Length;
            }

            public bool TryDequeue(out byte[] data)
            {
                if (_inFlight.Count > 0 && _inFlight.Peek().deliverAt <= _network.Time)
                {
                    data = _inFlight.Dequeue().data;
                    return true;
                }

                data = null;
                return false;
            }
        }

        private class Endpoint : IChannel
        {
            private readonly Pipe _outgoing;
            private readonly Pipe _incoming;

            public Endpoint(Pipe outgoing, Pipe incoming)
            {
                _outgoing = outgoing;
                _incoming = incoming;
            }

            public long BytesSent => _outgoing.BytesSent;
            public void Send(byte[] data) => _outgoing.Enqueue(data);
            public bool TryReceive(out byte[] data) => _incoming.TryDequeue(out data);
        }
    }
}
