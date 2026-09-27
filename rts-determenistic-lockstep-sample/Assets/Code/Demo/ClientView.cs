using System.Collections.Generic;
using Rts.Lockstep.Code.Core;
using UnityEngine;

namespace Rts.Lockstep.Demo.Code.Demo
{
    /// <summary>
    /// Renders one LockstepClient's world and turns mouse input into Commands.
    /// Pure presentation: reads the World, never writes to it.
    /// </summary>
    public class ClientView : MonoBehaviour
    {
        private static readonly Color[] PlayerColors =
        {
            new Color(0.25f, 0.55f, 1f),
            new Color(1f, 0.35f, 0.3f),
            new Color(0.3f, 0.85f, 0.4f),
            new Color(1f, 0.8f, 0.2f),
        };

        private readonly Dictionary<int, Renderer> _unitViews = new Dictionary<int, Renderer>();
        private readonly List<int> _selection = new List<int>();
        private readonly List<int> _toRemove = new List<int>();

        private LockstepClient _client;
        private Camera _camera;
        private Transform _clickMarker;
        private float _clickMarkerTime;
        private Vector3? _dragStart;

        public LockstepClient Client => _client;
        public Camera Camera => _camera;

        public void Init(LockstepClient client, Rect viewport)
        {
            _client = client;

            var cameraGo = new GameObject("Camera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.transform.localPosition = new Vector3(0, 30, -22);
            cameraGo.transform.LookAt(transform.position);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.rect = viewport;
            _camera.fieldOfView = 45;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.12f, 0.13f, 0.15f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(3.2f, 1, 2);
            ground.GetComponent<Renderer>().material.color = new Color(0.3f, 0.33f, 0.3f);

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "ClickMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = new Vector3(0.6f, 0.02f, 0.6f);
            marker.GetComponent<Renderer>().material.color = Color.green;
            marker.SetActive(false);
            _clickMarker = marker.transform;
        }

        private void Update()
        {
            if (_client == null || !_client.IsJoined)
                return;

            HandleInput();
        }

        private void LateUpdate()
        {
            if (_client == null || !_client.IsJoined)
                return;

            // LateUpdate: the client has already stepped its ticks for this frame.
            SyncUnitViews();
            _clickMarker.gameObject.SetActive(Time.time < _clickMarkerTime);
        }

        // ---------------------------------------------------------------- rendering

        private void SyncUnitViews()
        {
            World world = _client.World;
            float alpha = _client.InterpolationAlpha;

            foreach (Unit unit in world.Units)
            {
                if (!_unitViews.TryGetValue(unit.Id, out Renderer view))
                {
                    view = CreateUnitView(unit);
                    _unitViews.Add(unit.Id, view);
                }

                // The simulation runs at 20 Hz, the screen at 60+ Hz: interpolate between the last two ticks.
                Vector3 from = ToLocal(_client.GetPreviousPosition(unit));
                Vector3 to = ToLocal(unit.Position);
                view.transform.localPosition = Vector3.Lerp(from, to, alpha) + Vector3.up * 0.5f;

                bool selected = _selection.Contains(unit.Id);
                view.transform.localScale = selected ? new Vector3(1.15f, 0.6f, 1.15f) : new Vector3(0.8f, 0.5f, 0.8f);
                view.material.color = selected ? Color.white : PlayerColor(unit.Owner);
            }

            // Views of units that are gone (e.g. after a snapshot reload).
            _toRemove.Clear();
            foreach (KeyValuePair<int, Renderer> pair in _unitViews)
                if (world.FindUnit(pair.Key) == null)
                    _toRemove.Add(pair.Key);
            foreach (int id in _toRemove)
            {
                Destroy(_unitViews[id].gameObject);
                _unitViews.Remove(id);
                _selection.Remove(id);
            }
        }

        private Renderer CreateUnitView(Unit unit)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Unit {unit.Id} (p{unit.Owner})";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            return go.GetComponent<Renderer>();
        }

        private static Color PlayerColor(byte owner) => PlayerColors[(owner - 1) % PlayerColors.Length];

        private Vector3 ToLocal(FixVec2 p) => new Vector3(p.X.ToFloat(), 0, p.Y.ToFloat());

        // ---------------------------------------------------------------- input

        private void HandleInput()
        {
            bool mouseInside = _camera.pixelRect.Contains(Input.mousePosition);

            if (mouseInside && Input.GetMouseButtonDown(0))
                _dragStart = Input.mousePosition;

            if (_dragStart.HasValue && Input.GetMouseButtonUp(0))
            {
                Select(_dragStart.Value, Input.mousePosition);
                _dragStart = null;
            }

            if (!mouseInside)
                return;

            if (Input.GetMouseButtonDown(1) && _selection.Count > 0 && TryGetGroundPoint(out Vector3 local))
            {
                // Float -> fixed at the input boundary; from here on it's an exact integer on every peer.
                var target = new FixVec2(Fix64.FromFloat(local.x), Fix64.FromFloat(local.z));
                _client.SendCommand(Command.Move(_selection.ToArray(), target));

                // Instant local feedback hides the lockstep round trip.
                _clickMarker.localPosition = local + Vector3.up * 0.02f;
                _clickMarkerTime = Time.time + 0.4f;
            }

            if (Input.GetKeyDown(KeyCode.S) && _selection.Count > 0)
                _client.SendCommand(Command.Stop(_selection.ToArray()));
        }

        private void Select(Vector3 screenFrom, Vector3 screenTo)
        {
            _selection.Clear();
            Rect box = ScreenRect(screenFrom, screenTo);
            bool isClick = box.width < 6 && box.height < 6;
            float bestDistance = 40f; // pixels
            int bestId = -1;

            foreach (Unit unit in _client.World.Units)
            {
                if (unit.Owner != _client.PlayerId || !_unitViews.TryGetValue(unit.Id, out Renderer view))
                    continue;

                Vector3 screen = _camera.WorldToScreenPoint(view.transform.position);
                if (isClick)
                {
                    float distance = Vector2.Distance(screen, screenTo);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestId = unit.Id;
                    }
                }
                else if (box.Contains(screen))
                {
                    _selection.Add(unit.Id);
                }
            }

            if (bestId >= 0)
                _selection.Add(bestId);
        }

        private bool TryGetGroundPoint(out Vector3 local)
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            var ground = new Plane(Vector3.up, transform.position);
            if (ground.Raycast(ray, out float enter))
            {
                local = transform.InverseTransformPoint(ray.GetPoint(enter));
                return true;
            }

            local = default;
            return false;
        }

        private static Rect ScreenRect(Vector3 a, Vector3 b) =>
            Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private void OnGUI()
        {
            if (!_dragStart.HasValue)
                return;

            // GUI space has Y pointing down.
            Rect box = ScreenRect(_dragStart.Value, Input.mousePosition);
            box.y = Screen.height - box.yMax;
            GUI.Box(box, GUIContent.none);
        }
    }
}
