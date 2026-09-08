using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// draws a closed polyline as UI geometry: a wide dark stroke underneath and a narrower light
    /// stroke on top, so the track outline reads over any scenery. one mesh, one draw call, no
    /// texture. points are in this rect's local space; MiniMapGUI does the world-to-map fitting.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class MiniMapTrackGraphic : MaskableGraphic
    {
        public float lineWidth = 5f;
        public float outlineWidth = 10f;
        public Color lineColor = new Color(0.85f, 0.92f, 1f, 1f);
        public Color outlineColor = new Color(0.05f, 0.08f, 0.12f, 0.9f);

        private readonly List<Vector2> points = new List<Vector2>();

        public int PointCount { get { return points.Count; } }

        public void SetPoints(IList<Vector2> loop)
        {
            points.Clear();
            points.AddRange(loop);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;
            Stroke(vh, outlineWidth, outlineColor);
            Stroke(vh, lineWidth, lineColor);
        }

        /// <summary>
        /// one quad per segment plus a small square at every vertex, which fills the notch that
        /// two butt-ended quads leave on the outside of a bend. cheap and good enough at HUD size
        /// </summary>
        private void Stroke(VertexHelper vh, float width, Color colour)
        {
            var half = width * 0.5f;
            var n = points.Count;
            for (int i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                var dir = b - a;
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();
                var normal = new Vector2(-dir.y, dir.x) * half;
                Quad(vh, a + normal, b + normal, b - normal, a - normal, colour);
                Quad(vh, a + new Vector2(-half, half), a + new Vector2(half, half),
                     a + new Vector2(half, -half), a + new Vector2(-half, -half), colour);
            }
        }

        private static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color colour)
        {
            var start = vh.currentVertCount;
            vh.AddVert(p0, colour, Vector2.zero);
            vh.AddVert(p1, colour, Vector2.zero);
            vh.AddVert(p2, colour, Vector2.zero);
            vh.AddVert(p3, colour, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
