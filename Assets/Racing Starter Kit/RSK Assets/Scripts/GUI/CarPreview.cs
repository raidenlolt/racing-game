using UnityEngine;
using UnityEngine.UI;

namespace SpinMotion
{
    /// <summary>
    /// renders the selected car into the menu as a slowly turning 3D model.
    ///
    /// it does not instantiate the car prefab. doing that would wake CarController, NitroSystem, the
    /// AI stack and the audio sources, all of which would run and make noise for a car that is only
    /// meant to be a picture. instead it mirrors just the mesh filters and renderers into a fresh
    /// hierarchy, which is inert by construction.
    ///
    /// the stage sits far below the track on its own layer, with a camera and light that render only
    /// that layer, so the preview cannot light or appear in the race and the race cannot appear in
    /// the preview.
    /// </summary>
    public class CarPreview : MonoBehaviour
    {
        public CarCatalogue carCatalogue;
        [Tooltip("Where the rendered preview is displayed in the menu")]
        public RawImage targetImage;

        [Header("Stage")]
        [Tooltip("Layer used exclusively by the preview. Must exist in the Tags and Layers settings.")]
        public string previewLayer = "CarPreview";
        [Tooltip("How far below the world the stage is built, well clear of the track and its safety floor")]
        public float stageDepth = -5000f;
        public int textureSize = 512;
        [Tooltip("Cleared behind the car. Transparent by default so the car floats over the menu instead of sitting in a black box, which is very visible once the panel is large.")]
        public Color background = new Color(0.06f, 0.07f, 0.09f, 0f);

        [Header("Framing")]
        [Tooltip("Closest the camera is ever allowed to sit, so a tiny model cannot put the lens inside the bodywork")]
        public float cameraDistance = 3f;
        public float cameraHeight = 4.5f;
        public float turnSpeed = 25f;
        [Tooltip("A long lens. Wide angles bow the bodywork outwards; this keeps the car looking like a product shot rather than a fisheye.")]
        public float fieldOfView = 30f;
        [Tooltip("How much of the frame the car fills. 1 would touch the edges as it turns.")]
        [Range(0.4f, 1f)] public float fillFraction = 0.82f;

        private GameObject stage;
        private GameObject model;
        private Camera previewCamera;
        private RenderTexture texture;
        private int shownIndex = -1;
        private int layer = -1;

        private void Awake()
        {
            layer = LayerMask.NameToLayer(previewLayer);
            if (layer < 0)
            {
                Debug.LogWarning("[CarPreview] layer '" + previewLayer +
                                 "' does not exist, the preview would light the race scene. Disabling.");
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (stage != null) Destroy(stage);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }

        /// <summary>swap the displayed car. cheap to call repeatedly with the same index</summary>
        public void Show(int index)
        {
            if (!enabled || carCatalogue == null || carCatalogue.Count == 0) return;
            if (index == shownIndex && model != null) return;

            EnsureStage();

            if (model != null) Destroy(model);
            var entry = carCatalogue.Get(index);
            if (entry == null || entry.playerPrefab == null) return;

            model = BuildVisualCopy(entry.playerPrefab);
            model.transform.SetParent(stage.transform, false);
            model.transform.localPosition = Vector3.zero;
            FrameModel();
            shownIndex = index;
        }

        private void Update()
        {
            if (model != null)
                model.transform.Rotate(Vector3.up, turnSpeed * Time.unscaledDeltaTime, Space.Self);
        }

        private void EnsureStage()
        {
            if (stage != null) return;

            stage = new GameObject("Car Preview Stage");
            stage.transform.position = new Vector3(0f, stageDepth, 0f);

            // match the render texture to the shape of the panel it lands in, otherwise a wide slot
            // stretches a square render and the car looks squashed
            var aspect = 1f;
            if (targetImage != null)
            {
                var rect = targetImage.rectTransform.rect;
                if (rect.height > 1f) aspect = Mathf.Clamp(rect.width / rect.height, 0.5f, 4f);
            }
            texture = new RenderTexture(Mathf.RoundToInt(textureSize * aspect), textureSize, 16);
            texture.Create();

            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(stage.transform, false);
            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = background;
            // render nothing but the preview layer, so the race can never bleed into this shot
            previewCamera.cullingMask = 1 << layer;
            previewCamera.targetTexture = texture;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 200f;

            var lightGo = new GameObject("Preview Light");
            lightGo.transform.SetParent(stage.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            // the matching half of the deal: this light touches only the preview layer, so putting a
            // directional light in the scene does not brighten the whole racetrack
            light.cullingMask = 1 << layer;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            if (targetImage != null) targetImage.texture = texture;
        }

        /// <summary>
        /// copies just the visible geometry out of a car prefab, preserving each renderer's placement
        /// relative to the prefab root
        /// </summary>
        private GameObject BuildVisualCopy(GameObject source)
        {
            var root = new GameObject("Preview Model");
            root.layer = layer;

            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(false))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;

                var piece = new GameObject(filter.gameObject.name);
                piece.layer = layer;
                piece.transform.SetParent(root.transform, false);

                // rebuild the placement relative to the prefab root rather than copying world values,
                // because the prefab asset itself is not positioned in the scene
                piece.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                piece.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                piece.transform.localScale = filter.transform.lossyScale;

                piece.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            return root;
        }

        /// <summary>
        /// points the camera at the model and pulls back just far enough to hold it in frame.
        ///
        /// the distance is solved against the car's eight bounding box corners rather than its
        /// bounding sphere. a sphere around a car is dominated by the car's LENGTH -- a 10m long,
        /// 3m tall body has a 6.3m radius -- so fitting the sphere to the frame height leaves the
        /// actual silhouette occupying a fraction of it, which is what made the car look like a
        /// thumbnail stranded in a large panel.
        ///
        /// both axes are solved, because the panel is far wider than it is tall and which one binds
        /// depends on the car and on the angle it has turned to.
        /// </summary>
        private void FrameModel()
        {
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            var any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return;

            previewCamera.fieldOfView = fieldOfView;

            var focus = bounds.center;
            // a three quarter view: front wing and flank both visible, which is how a car is sold
            var direction = new Vector3(0.75f, 0.42f, -0.75f).normalized;
            var rotation = Quaternion.LookRotation(-direction, Vector3.up);

            var tanVertical = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            var tanHorizontal = tanVertical * Mathf.Max(0.1f, previewCamera.aspect);

            // for every corner, how far back the camera must sit for that corner to stay inside each
            // edge of the frame. the furthest requirement wins.
            var needed = cameraDistance;
            var extents = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);

                // into camera axes, measured from the focus point. the camera sits behind the focus
                // looking forwards, so a corner's depth from the lens is (distance + local.z), not
                // minus: a corner nearer the camera has a NEGATIVE z here and needs more room, not less
                var local = Quaternion.Inverse(rotation) * (corner - focus);
                needed = Mathf.Max(needed, -local.z + Mathf.Abs(local.y) / (tanVertical * fillFraction));
                needed = Mathf.Max(needed, -local.z + Mathf.Abs(local.x) / (tanHorizontal * fillFraction));
            }

            previewCamera.transform.position = focus + direction * needed;
            previewCamera.transform.rotation = rotation;

            CentreInFrame(bounds, needed, tanVertical, tanHorizontal);
        }

        /// <summary>
        /// nudges the camera so the car sits in the middle of the panel.
        ///
        /// aiming at the bounding box centre does not centre the PICTURE of the car: under
        /// perspective the corners nearest the lens project further out than the ones behind, so a
        /// three quarter view lands low and to one side. this measures where the silhouette actually
        /// landed and slides the camera sideways and up by the difference.
        /// </summary>
        private void CentreInFrame(Bounds bounds, float distance, float tanVertical, float tanHorizontal)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            var extents = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);

                var viewport = previewCamera.WorldToViewportPoint(corner);
                min = Vector2.Min(min, viewport);
                max = Vector2.Max(max, viewport);
            }

            var centre = (min + max) * 0.5f;
            var error = centre - new Vector2(0.5f, 0.5f);

            // a full frame of viewport spans 2*d*tan in world units at the car's depth
            var worldOffset = previewCamera.transform.right * (error.x * 2f * distance * tanHorizontal)
                            + previewCamera.transform.up * (error.y * 2f * distance * tanVertical);

            previewCamera.transform.position += worldOffset;
        }
    }
}
