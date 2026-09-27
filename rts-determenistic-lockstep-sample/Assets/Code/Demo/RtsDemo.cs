using System.Collections.Generic;
using System.Diagnostics;
using Rts.Lockstep.Code.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rts.Lockstep.Demo.Code.Demo
{
    public class RtsDemo : MonoBehaviour
    {
        [Header("Client A link (one-way)")] 
        [SerializeField] private int latencyA = 40;
        [SerializeField] private int jitterA = 10;
        [Header("Client B link (one-way)")] 
        [SerializeField] private int latencyB = 150;
        [SerializeField] private int jitterB = 50;

        private readonly List<string> _log = new();
        private SimulatedNetwork _network;
        private LockstepServer _server;
        private Slot _a;
        private Slot _b;
        
        private void Start()
        {
            _network = new SimulatedNetwork();
            _server = new LockstepServer();
            _server.Log += AddLog;

            _a = CreateSlot("Client A", new LinkSettings(latencyA, jitterA), seed: 1,
                origin: Vector3.zero, viewport: new Rect(0, 0, 0.5f, 1));
            _b = CreateSlot("Client B", new LinkSettings(latencyB, jitterB), seed: 2,
                origin: new Vector3(1000, 0, 0), viewport: new Rect(0.5f, 0, 0.5f, 1));

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightInstance = new GameObject("Directional Light").AddComponent<Light>();
                lightInstance.type = LightType.Directional;
                lightInstance.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            // Client B stays out on purpose: press "Join" to see a late join via snapshot.
            _a.Client.Join();
            InvokeRepeating(nameof(SampleBandwidth), 1, 1);
        }

        private Slot CreateSlot(string slotName, LinkSettings link, int seed, Vector3 origin, Rect viewport)
        {
            _network.Connect(link, seed, out IChannel clientSide, out IChannel serverSide);
            _server.AddConnection(serverSide);

            var client = new LockstepClient(clientSide);
            client.Log += AddLog;

            var view = new GameObject(slotName).AddComponent<ClientView>();
            view.transform.position = origin;
            view.Init(client, viewport);

            return new Slot { Name = slotName, Link = link, ServerSide = serverSide, Client = client, View = view };
        }

        private void Update()
        {
            _a.Link.LatencyMs = latencyA;
            _a.Link.JitterMs = jitterA;
            _b.Link.LatencyMs = latencyB;
            _b.Link.JitterMs = jitterB;

            // One "real time" source for everybody. Each side still advances in fixed ticks internally.
            double dt = Time.deltaTime;
            _network.Advance(dt);
            _server.Update(dt);
            _a.Client.Update(dt);
            _b.Client.Update(dt);
        }

        private void SampleBandwidth()
        {
            foreach (var slot in new[] { _a, _b })
            {
                slot.BytesPerSecond = slot.ServerSide.BytesSent - slot.LastBytes;
                slot.LastBytes = slot.ServerSide.BytesSent;
            }
        }

        private void AddLog(string line)
        {
            Debug.Log(line);
            _log.Add($"{Time.time:0.0}s {line}");
            if (_log.Count > 8)
                _log.RemoveAt(0);
        }

        // ---------------------------------------------------------------- debug UI
        [Conditional("DEBUG")]
        private void OnGUI()
        {
            if (_server == null)
                return;

            DrawClientPanel(_a, 10, ref latencyA, ref jitterA);
            DrawClientPanel(_b, Screen.width / 2 + 10, ref latencyB, ref jitterB);

            GUILayout.BeginArea(new Rect(10, Screen.height - 190, Screen.width - 20, 180), GUI.skin.box);
            GUILayout.Label($"SERVER  tick {_server.World.Tick}   desyncs detected: {_server.DesyncCount}   " +
                            $"({Simulation.TicksPerSecond} ticks/s)      LMB: select / drag box   RMB: move   S: stop");
            foreach (string line in _log)
                GUILayout.Label(line);
            GUILayout.EndArea();
        }

        [Conditional("DEBUG")]
        private void DrawClientPanel(Slot slot, float x, ref int latency, ref int jitter)
        {
            LockstepClient client = slot.Client;
            GUILayout.BeginArea(new Rect(x, 10, 300, 230), GUI.skin.box);

            if (!client.IsJoined)
            {
                GUILayout.Label(slot.Name + " - not connected");
                if (GUILayout.Button("Join (late join via snapshot)"))
                    client.Join();
            }
            else
            {
                var behind = _server.World.Tick - client.World.Tick;
                GUILayout.Label($"{slot.Name} - player {client.PlayerId}");
                GUILayout.Label($"Tick {client.World.Tick}   ({behind} behind server)");
                GUILayout.Label($"Buffered frames: {client.BufferedFrames}   Stalls: {client.StallCount}" +
                                (client.IsStalled ? "   <color=red>STALLED</color>" : ""));
                GUILayout.Label($"Hash: {client.World.ComputeHash():X8}   Down: {slot.BytesPerSecond:0} B/s");
                if (GUILayout.Button("Corrupt local state (force desync)"))
                    client.CorruptStateForDemo();
            }

            GUILayout.Label($"Latency: {latency} ms (RTT {latency * 2})");
            latency = (int)GUILayout.HorizontalSlider(latency, 0, 500);
            GUILayout.Label($"Jitter: {jitter} ms");
            jitter = (int)GUILayout.HorizontalSlider(jitter, 0, 200);
            GUILayout.EndArea();
        }

        /// <summary>Scene view: yellow wire spheres = server's (authoritative, slightly ahead) world.</summary>
        [Conditional("DEBUG")]
        private void OnDrawGizmos()
        {
            if (_server == null)
                return;

            Gizmos.color = Color.yellow;
            foreach (var slot in new[] { _a, _b })
            foreach (var unit in _server.World.Units)
            {
                var local = new Vector3(unit.Position.X.ToFloat(), 0.5f, unit.Position.Y.ToFloat());
                Gizmos.DrawWireSphere(slot.View.transform.position + local, 0.5f);
            }
        }
    }
}
