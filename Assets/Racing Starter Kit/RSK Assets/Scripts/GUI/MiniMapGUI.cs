using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// the mini map on the race HUD: the circuit as a line, every car as a dot, the player as an
    /// arrow. the circuit is traced from the track's AI waypoint loop, which every track already
    /// carries for the bots and the respawn logic, so one HUD prefab covers all maps with no
    /// per-scene camera or artwork. waypoints are 40 to 130 m apart, so the loop is smoothed with a
    /// Catmull-Rom curve before it is drawn or the map would be a polygon.
    ///
    /// north-up and fixed, so the shape a player learns on one lap is the same shape on the next.
    /// </summary>
    public class MiniMapGUI : MonoBehaviour
    {
        public GameEvents gameEvents;
        public RealTimeRacePositionsItem realTimeRacePositions;
        public AIWaypointSet aiWaypointSet;

        [Header("Drawing, wired by the setup pass")]
        public MiniMapTrackGraphic track;
        [Tooltip("Rect the circuit is fitted into. Defaults to the track graphic's rect.")]
        public RectTransform mapArea;

        [Header("Fit")]
        [Tooltip("Pixels of breathing room between the circuit and the edge of the map area")]
        public float padding = 16f;
        [Tooltip("Curve samples per waypoint segment")]
        [Range(1, 12)] public int subdivisions = 6;

        [Header("Markers")]
        public Color playerColor = new Color(1f, 0.62f, 0.2f);
        [Tooltip("Other cars. Cyan by client request, so they read against both the dark map and the white track line.")]
        public Color botColor = new Color(0f, 1f, 1f, 1f);
        public Color finishColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Marker sizes in reference pixels (1600x900 canvas). The map is small on a phone, so these are generous.")]
        public float playerSize = 22f;
        public float botSize = 16f;

        public int MarkerCount { get { return markers.Count; } }
        public bool HasTrack { get { return track != null && track.PointCount > 2; } }

        private readonly List<Transform> cars = new List<Transform>();
        private readonly List<RectTransform> markers = new List<RectTransform>();
        private RectTransform finishMarker;
        private Sprite dotSprite;
        private Sprite arrowSprite;
        private Vector2 worldMin, worldMax;
        private float scale;
        private Vector2 offset;
        private bool fitted;

        private void Awake()
        {
            if (mapArea == null && track != null) mapArea = track.rectTransform;
            dotSprite = BuildDot(32);
            arrowSprite = BuildArrow(32);

            if (gameEvents == null) return;
            gameEvents.PlayersCheckpointTrackersAssignedEvent.AddListener(OnCarsAssigned);
            gameEvents.PreRaceUpdateGuiEvent.AddListener(OnPreRace);
        }

        private void OnDestroy()
        {
            if (gameEvents == null) return;
            gameEvents.PlayersCheckpointTrackersAssignedEvent.RemoveListener(OnCarsAssigned);
            gameEvents.PreRaceUpdateGuiEvent.RemoveListener(OnPreRace);
        }

        private void OnEnable()
        {
            // the HUD is switched on after the waypoints registered themselves in Start, so this
            // is the first safe moment to trace the circuit; also covers a restart
            if (!fitted) BuildTrack();
        }

        private void OnPreRace()
        {
            if (!fitted) BuildTrack();
        }

        /// <summary>the spawner hands over one tracker per car, index 0 the player</summary>
        private void OnCarsAssigned(List<CheckpointTracker> trackers)
        {
            cars.Clear();
            foreach (var t in trackers)
                cars.Add(t != null ? t.transform.root : null);
            EnsureMarkers();
        }

        private void BuildTrack()
        {
            if (track == null || aiWaypointSet == null || aiWaypointSet.Items.Count < 3) return;

            var loop = new List<Vector2>();
            foreach (var wp in aiWaypointSet.Items)
                if (wp != null && wp.aiWaypointTransform != null)
                    loop.Add(new Vector2(wp.aiWaypointTransform.position.x, wp.aiWaypointTransform.position.z));
            if (loop.Count < 3) return;

            worldMin = new Vector2(float.MaxValue, float.MaxValue);
            worldMax = new Vector2(float.MinValue, float.MinValue);
            var smooth = Smooth(loop, Mathf.Max(1, subdivisions));
            foreach (var p in smooth)
            {
                worldMin = Vector2.Min(worldMin, p);
                worldMax = Vector2.Max(worldMax, p);
            }
            Fit();

            var mapped = new List<Vector2>(smooth.Count);
            foreach (var p in smooth) mapped.Add(ToMap(p));
            track.SetPoints(mapped);

            // the start/finish gate is checkpoint 1; fall back to the first waypoint on a track
            // whose checkpoints have not registered yet
            var finishAt = loop[0];
            var finishNext = loop[1 % loop.Count];
            var checkpoints = FindFirstObjectByType<Checkpoints>();
            if (checkpoints != null && checkpoints.checkpoints.Count > 0 && checkpoints.checkpoints[0] != null)
            {
                var gate = checkpoints.checkpoints[0].transform;
                finishAt = new Vector2(gate.position.x, gate.position.z);
                finishNext = finishAt + new Vector2(gate.forward.x, gate.forward.z);
            }
            PlaceFinishMarker(finishAt, finishNext);
            fitted = true;
        }

        /// <summary>uniform scale that fits the circuit's bounds into the map area, centred</summary>
        private void Fit()
        {
            var area = mapArea != null ? mapArea.rect : new Rect(-100, -100, 200, 200);
            var size = worldMax - worldMin;
            var usable = new Vector2(Mathf.Max(1f, area.width - padding * 2f), Mathf.Max(1f, area.height - padding * 2f));
            scale = Mathf.Min(usable.x / Mathf.Max(1f, size.x), usable.y / Mathf.Max(1f, size.y));
            var centre = (worldMin + worldMax) * 0.5f;
            offset = area.center - centre * scale;
        }

        private Vector2 ToMap(Vector2 world)
        {
            return world * scale + offset;
        }

        private static List<Vector2> Smooth(List<Vector2> loop, int steps)
        {
            var result = new List<Vector2>(loop.Count * steps);
            var n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                var p0 = loop[(i - 1 + n) % n];
                var p1 = loop[i];
                var p2 = loop[(i + 1) % n];
                var p3 = loop[(i + 2) % n];
                for (int s = 0; s < steps; s++)
                {
                    var t = s / (float)steps;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    var point = 0.5f * ((2f * p1) + (-p0 + p2) * t
                                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    result.Add(point);
                }
            }
            return result;
        }

        private void PlaceFinishMarker(Vector2 at, Vector2 next)
        {
            if (finishMarker == null)
            {
                finishMarker = NewMarker("Finish Line", null, finishColor, new Vector2(16f, 4f));
                finishMarker.SetAsFirstSibling();
            }
            var dir = (next - at).normalized;
            finishMarker.anchoredPosition = ToMap(at);
            // a short bar across the line, perpendicular to the direction of travel
            finishMarker.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
        }

        private void EnsureMarkers()
        {
            while (markers.Count < cars.Count)
            {
                var index = markers.Count;
                var isPlayer = index == 0;
                var marker = NewMarker(isPlayer ? "Player Marker" : "Bot Marker " + index,
                                       isPlayer ? arrowSprite : dotSprite,
                                       isPlayer ? playerColor : botColor,
                                       Vector2.one * (isPlayer ? playerSize : botSize));
                markers.Add(marker);
            }
            for (int i = 0; i < markers.Count; i++)
                markers[i].gameObject.SetActive(i < cars.Count && cars[i] != null);
            // the player draws over the pack
            if (markers.Count > 0) markers[0].SetAsLastSibling();
        }

        private RectTransform NewMarker(string name, Sprite sprite, Color colour, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(mapArea != null ? mapArea : transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            return rect;
        }

        private void LateUpdate()
        {
            if (!fitted)
            {
                BuildTrack();
                if (!fitted) return;
            }

            for (int i = 0; i < markers.Count && i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null) continue;
                // a finished bot is hidden; its dot goes with it
                var shown = car.gameObject.activeInHierarchy;
                if (markers[i].gameObject.activeSelf != shown) markers[i].gameObject.SetActive(shown);
                if (!shown) continue;
                var p = car.position;
                markers[i].anchoredPosition = ToMap(new Vector2(p.x, p.z));
                if (i == 0)
                {
                    // world yaw 0 points +z, which is up on the map; UI rotation is counter-clockwise
                    markers[i].localRotation = Quaternion.Euler(0f, 0f, -car.eulerAngles.y);
                }
            }
        }

        /// <summary>
        /// a disc with a dark rim. the image colour tints the white centre (cyan for the pack) while
        /// the rim stays dark whatever the tint, so the dot separates from the pale track line it
        /// spends most of the race sitting on. the first version was a plain soft-edged disc and
        /// vanished into the line on a phone
        /// </summary>
        private static Sprite BuildDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            const float rimStart = 0.66f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                var a = 1f - Edge(0.9f, 1f, d);
                // centre white (tinted), rim dark; a short blend between them keeps the edge clean
                var rim = Edge(rimStart, rimStart + 0.12f, d);
                var shade = (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 18f, rim));
                pixels[y * size + x] = new Color32(shade, shade, shade, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// the shader smoothstep: 0 below edge0, 1 above edge1, a smooth ramp between. this is NOT
        /// Mathf.SmoothStep, which interpolates between two values by a 0..1 t and was what the
        /// dot used to be drawn with: 1 - Mathf.SmoothStep(0.78, 1, d) peaks at 0.22, so the pack's
        /// dots were drawn at 22 percent alpha and never showed against the track line
        /// </summary>
        private static float Edge(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>a chevron pointing up (+y), with a soft edge</summary>
        private static Sprite BuildArrow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // unit space: x -1..1, y -1..1, tip at (0, 1), base corners at (±0.85, -0.8),
                // notch at (0, -0.35) so it reads as an arrow rather than a triangle
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                var inTriangle = v >= -0.8f && Mathf.Abs(u) <= 0.85f * (1f - v) / 1.8f;
                // bite a notch out of the base so it reads as a chevron, not a plain triangle
                var inNotch = v < -0.35f - Mathf.Abs(u) * 0.55f;
                var inside = inTriangle && !inNotch;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
