using System;
using System.Collections.Generic;
using System.IO;

namespace Rts.Lockstep.Code.Core
{
    /// <summary>
    /// Lockstep client. It never decides anything on its own:
    ///  - player input is sent to the server, NOT applied locally;
    ///  - tick N is simulated only after frame N arrives from the server;
    ///  - no frame yet -> the client waits (stalls) instead of guessing.
    /// That's the price of lockstep: input delay = round trip to the server.
    /// RTS games hide it with instant feedback (a click marker, a "yes sir!" voice line).
    /// </summary>
    public class LockstepClient
    {
        private const int HashIntervalTicks = 10;

        public event Action<string> Log;

        public World World { get; private set; }
        public byte PlayerId { get; private set; }
        public bool IsJoined => World != null;
        public bool IsStalled { get; private set; } = true;
        public int StallCount { get; private set; }
        public int BufferedFrames => _frames.Count;

        /// <summary>Jitter buffer: after a stall wait for this many frames, so playback doesn't stutter.</summary>
        public int TargetBufferedFrames = 2;

        /// <summary>More frames than this queued (e.g. after a lag spike) -> fast-forward to catch up.</summary>
        public int MaxBufferedFrames = 6;

        private readonly IChannel _channel;
        private readonly Queue<TickFrame> _frames = new Queue<TickFrame>();
        private readonly Dictionary<int, FixVec2> _previousPositions = new Dictionary<int, FixVec2>(); // rendering only
        private double _accumulator;
        private bool _refillingBuffer = true;

        public LockstepClient(IChannel channel)
        {
            _channel = channel;
        }

        /// <summary>0..1 progress between the previous and the current tick, for smooth rendering.</summary>
        public float InterpolationAlpha => (float)Math.Min(_accumulator / Simulation.TickDuration, 1.0);

        public void Join() => _channel.Send(Protocol.Join());

        public void SendCommand(Command command) => _channel.Send(Protocol.Command(command));

        public FixVec2 GetPreviousPosition(Unit unit) =>
            _previousPositions.TryGetValue(unit.Id, out FixVec2 position) ? position : unit.Position;

        public void Update(double deltaTime)
        {
            while (_channel.TryReceive(out byte[] data))
                Handle(data);

            if (World == null)
                return;

            _accumulator += deltaTime;

            if (_refillingBuffer && _frames.Count < TargetBufferedFrames)
            {
                SetStalled(true);
                _accumulator = Math.Min(_accumulator, Simulation.TickDuration);
                return;
            }

            _refillingBuffer = false;

            while (_accumulator >= Simulation.TickDuration)
            {
                if (_frames.Count == 0)
                {
                    // Starved: the server's next frame is late. Freeze rather than predict.
                    SetStalled(true);
                    _refillingBuffer = true;
                    _accumulator = Simulation.TickDuration; // don't pile up time debt while frozen
                    return;
                }

                _accumulator -= Simulation.TickDuration;
                StepFrame(_frames.Dequeue());
            }

            while (_frames.Count > MaxBufferedFrames)
                StepFrame(_frames.Dequeue());

            SetStalled(false);
        }

        /// <summary>Demo helper: breaks determinism on purpose so the desync detection kicks in.</summary>
        public void CorruptStateForDemo()
        {
            if (World == null || World.Units.Count == 0)
                return;

            Unit unit = World.Units[0];
            unit.Position += new FixVec2(Fix64.FromRaw(1), Fix64.Zero); // 1/65536 of a meter is enough
            Log?.Invoke($"[Client p{PlayerId}] corrupted unit {unit.Id} locally");
        }

        private void StepFrame(TickFrame frame)
        {
            if (frame.Tick != World.Tick)
                throw new InvalidOperationException($"Expected frame {World.Tick}, got {frame.Tick}");

            _previousPositions.Clear();
            foreach (Unit unit in World.Units)
                _previousPositions[unit.Id] = unit.Position;

            Simulation.Step(World, frame.Commands);

            if (World.Tick % HashIntervalTicks == 0)
                _channel.Send(Protocol.StateHash(World.Tick, World.ComputeHash()));
        }

        private void Handle(byte[] data)
        {
            using (BinaryReader reader = Protocol.Open(data, out MessageType type))
            {
                switch (type)
                {
                    case MessageType.Snapshot:
                        PlayerId = reader.ReadByte();
                        World = World.Read(reader);
                        _previousPositions.Clear();

                        // Frames older than the snapshot are already baked into it.
                        while (_frames.Count > 0 && _frames.Peek().Tick < World.Tick)
                            _frames.Dequeue();

                        Log?.Invoke($"[Client p{PlayerId}] loaded snapshot at tick {World.Tick}");
                        break;

                    case MessageType.TickFrame:
                        TickFrame frame = TickFrame.Read(reader);
                        if (World != null && frame.Tick >= World.Tick)
                            _frames.Enqueue(frame);
                        break;
                }
            }
        }

        private void SetStalled(bool stalled)
        {
            if (stalled && !IsStalled)
                StallCount++;
            IsStalled = stalled;
        }
    }
}