using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityAR
{
    /// <summary>
    /// Saves and restores the room (its mesh and the placed cubes) and the world origin using pre-registered images
    /// (scan targets).
    /// </summary>
    /// <remarks>
    /// <para>Images are recognized whenever they are in view and drawn with an outline, but nothing is saved or
    /// loaded until the user arms an action with <see cref="ToggleSave"/> or <see cref="ToggleLoad"/>. While an
    /// action is armed, a scan frame is shown in the center of view, and the action runs on the first image the user
    /// looks at through it.</para>
    /// <para>Saving makes the image's center and heading the world origin, then saves the current room mesh, the
    /// placed cubes and the origin relative to the image. Loading shows the room mesh saved to the image in place of
    /// live scanning, replaces the placed cubes with the saved ones and puts the world origin back where it was
    /// saved. Either way the image must stay in the same place in the room between sessions.</para>
    /// </remarks>
    public class ImageTargetController : MonoBehaviour
    {
        /// <summary>
        /// What happens the next time a scan target is recognized.
        /// </summary>
        public enum ImageAction
        {
            None,
            Save,
            Load,
        }

        class Target
        {
            public ARTrackedImage image;
            public LineRenderer outline;
            public float trackedSince = -1f;
            public bool hasPose;
            public Vector3 position;
            public Quaternion rotation;
            public bool announced;
            public float highlightUntil;
        }

        const string k_KeyPrefix = "image-";
        const float k_PoseSmoothingTime = 0.25f;
        const float k_OutlineWidth = 0.004f;
        const float k_HighlightDuration = 2f;
        static readonly Color k_RecognizedColor = new Color(0.3f, 0.85f, 1f);
        static readonly Color k_HighlightColor = new Color(0.4f, 1f, 0.4f);

        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        ARTrackedImageManager m_TrackedImageManager;

        [SerializeField]
        WorldOrigin m_WorldOrigin;

        [SerializeField]
        RoomMeshStore m_Store;

        [SerializeField]
        StatusToast m_Status;

        [SerializeField]
        ScanFrame m_ScanFrame;

        [SerializeField]
        [Tooltip("Material for the outline drawn on recognized images. Must use vertex colors.")]
        Material m_OutlineMaterial;

        [SerializeField]
        [Tooltip("How long an image must be tracked continuously before it is used, in seconds.")]
        float m_SettleTime = 0.6f;

        readonly Dictionary<TrackableId, Target> m_Targets = new Dictionary<TrackableId, Target>();
        readonly List<TrackableId> m_RemovedTargets = new List<TrackableId>();

        /// <summary>
        /// The action armed for the next recognized image.
        /// </summary>
        public ImageAction armedAction { get; private set; }

        /// <summary>
        /// Arms saving to the next recognized image, or disarms it.
        /// </summary>
        public void ToggleSave() => Arm(armedAction == ImageAction.Save ? ImageAction.None : ImageAction.Save);

        /// <summary>
        /// Arms loading from the next recognized image, or disarms it.
        /// </summary>
        public void ToggleLoad() => Arm(armedAction == ImageAction.Load ? ImageAction.None : ImageAction.Load);

        void Arm(ImageAction action)
        {
            armedAction = action;
            if (action == ImageAction.None)
                m_ScanFrame.Hide();
            else
                m_ScanFrame.Show();
            m_Status.Show(action switch
            {
                ImageAction.Save => "Look at a scan image through the frame to make it the origin and save the room to it.",
                ImageAction.Load => "Look at a scan image through the frame to load the room and origin saved to it.",
                _ => "Cancelled.",
            });
        }

        void Start()
        {
            m_Status.Show("Pinch to place a cube. To save the room and cubes, press Save to image and look at Scan A. In a later session, press Load from image and look at it again.");
        }

        void Update()
        {
            Target ready = null;
            var readyDistance = float.MaxValue;
            var cameraPosition = m_XROrigin.Camera.transform.position;
            foreach (var image in m_TrackedImageManager.trackables)
            {
                var target = UpdateTarget(image);
                if (target == null || (armedAction != ImageAction.None && !m_ScanFrame.Contains(image.transform.position)))
                    continue;

                // The closest settled image is most likely the one the user is looking at.
                var distance = Vector3.Distance(cameraPosition, image.transform.position);
                if (distance < readyDistance)
                {
                    ready = target;
                    readyDistance = distance;
                }
            }

            RemoveDestroyedTargets();

            if (ready != null && armedAction != ImageAction.None)
            {
                var action = armedAction;
                armedAction = ImageAction.None;
                m_ScanFrame.Hide();
                if (action == ImageAction.Save)
                    Save(ready);
                else
                    Load(ready);
            }
        }

        // Returns the target if it has been tracked long enough to use.
        Target UpdateTarget(ARTrackedImage image)
        {
            if (!m_Targets.TryGetValue(image.trackableId, out var target))
            {
                target = new Target { image = image, outline = CreateOutline(image) };
                m_Targets.Add(image.trackableId, target);
            }

            var tracking = image.trackingState == TrackingState.Tracking;
            target.outline.enabled = tracking;
            if (!tracking)
            {
                target.trackedSince = -1f;
                return null;
            }

            if (target.trackedSince < 0f)
                target.trackedSince = Time.time;

            UpdatePose(target);
            UpdateOutline(target);

            if (Time.time - target.trackedSince < m_SettleTime)
                return null;

            if (!target.announced)
            {
                target.announced = true;
                Debug.Log($"[ImageTarget] Recognized '{GetName(target)}' at {target.position:F3} (session space)");
                if (armedAction == ImageAction.None)
                    m_Status.Show($"Recognized '{GetName(target)}'. Use the phone's image buttons to save the room to it or load it.");
            }
            return target;
        }

        void Save(Target target)
        {
            var targetName = GetName(target);
            try
            {
                // Make the image the origin first, so the saved origin is the image itself.
                m_WorldOrigin.SetOrigin(GetWorldPose(target));
                if (!m_Store.hasContent)
                {
                    m_Status.Show($"Origin set to '{targetName}'. Scan the room or place cubes first, then save to the image again.");
                    return;
                }

                var data = m_Store.Save(GetKey(target), GetWorldPose(target));
                Highlight(target);
                Debug.Log($"[ImageTarget] Saved '{targetName}' with {data.triangleCount} triangles and {data.cubes.Count} cubes");
                m_Status.Show($"Origin set to '{targetName}' and the room saved to it ({data.triangleCount:N0} triangles, {CountCubes(data)}).");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Status.Show($"Could not save to '{targetName}': {exception.Message}");
            }
        }

        void Load(Target target)
        {
            var targetName = GetName(target);
            var key = GetKey(target);
            if (!m_Store.HasSavedMesh(key))
            {
                m_Status.Show($"Nothing is saved to '{targetName}' yet.");
                return;
            }

            try
            {
                var anchorInSession = GetLevelPose(target);
                var data = m_Store.Load(key, m_XROrigin.TrackablesParent, anchorInSession);
                var anchor = GetWorldPose(target);
                m_WorldOrigin.SetOrigin(new Pose(
                    anchor.position + anchor.rotation * data.originPose.position,
                    anchor.rotation * data.originPose.rotation));
                Highlight(target);
                Debug.Log($"[ImageTarget] Loaded '{targetName}': {data.triangleCount} triangles, {data.cubes.Count} cubes");
                m_Status.Show($"Loaded the room ({CountCubes(data)}) and origin saved to '{targetName}' on {data.savedAtUtc.ToLocalTime():g}.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Status.Show($"Could not load from '{targetName}': {exception.Message}");
            }
        }

        void UpdatePose(Target target)
        {
            // Session space (relative to the trackables parent) is unaffected by moving the world origin.
            var trackables = m_XROrigin.TrackablesParent;
            var position = trackables.InverseTransformPoint(target.image.transform.position);
            var rotation = Quaternion.Inverse(trackables.rotation) * target.image.transform.rotation;

            if (!target.hasPose)
            {
                target.position = position;
                target.rotation = rotation;
                target.hasPose = true;
                return;
            }

            // Smooth out frame-to-frame jitter in the image pose.
            var t = 1f - Mathf.Exp(-Time.deltaTime / k_PoseSmoothingTime);
            target.position = Vector3.Lerp(target.position, position, t);
            target.rotation = Quaternion.Slerp(target.rotation, rotation, t);
        }

        // The image's position and heading in session space, ignoring its tilt. Image tilt estimates are noisy, and
        // even a small tilt error swings distant walls noticeably.
        static Pose GetLevelPose(Target target)
        {
            // Use whichever of the image's normal and its in-plane up axis is closer to horizontal, so this works both
            // for images lying flat and for images standing upright.
            var normal = target.rotation * Vector3.up;
            var imageUp = target.rotation * Vector3.forward;
            var heading = Mathf.Abs(normal.y) > Mathf.Abs(imageUp.y) ? imageUp : normal;
            heading.y = 0f;
            return new Pose(target.position, Quaternion.LookRotation(heading.normalized, Vector3.up));
        }

        Pose GetWorldPose(Target target)
        {
            var pose = GetLevelPose(target);
            var trackables = m_XROrigin.TrackablesParent;
            return new Pose(trackables.TransformPoint(pose.position), trackables.rotation * pose.rotation);
        }

        static string CountCubes(RoomMeshData data) => data.cubes.Count == 1 ? "1 cube" : $"{data.cubes.Count} cubes";

        static string GetName(Target target) => target.image.referenceImage.name;

        static string GetKey(Target target) => k_KeyPrefix + GetName(target);

        void Highlight(Target target) => target.highlightUntil = Time.time + k_HighlightDuration;

        void RemoveDestroyedTargets()
        {
            m_RemovedTargets.Clear();
            foreach (var pair in m_Targets)
            {
                if (pair.Value.image == null)
                    m_RemovedTargets.Add(pair.Key);
            }
            foreach (var id in m_RemovedTargets)
                m_Targets.Remove(id);
        }

        LineRenderer CreateOutline(ARTrackedImage image)
        {
            var outlineObject = new GameObject("Scan Target Outline");
            outlineObject.transform.SetParent(image.transform, false);

            var outline = outlineObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 4;
            outline.widthMultiplier = k_OutlineWidth;
            outline.sharedMaterial = m_OutlineMaterial;
            outline.shadowCastingMode = ShadowCastingMode.Off;
            outline.receiveShadows = false;
            outline.enabled = false;
            return outline;
        }

        void UpdateOutline(Target target)
        {
            // The image lies in its local XZ plane.
            var halfSize = target.image.size * 0.5f;
            var outline = target.outline;
            outline.SetPosition(0, new Vector3(-halfSize.x, 0f, -halfSize.y));
            outline.SetPosition(1, new Vector3(halfSize.x, 0f, -halfSize.y));
            outline.SetPosition(2, new Vector3(halfSize.x, 0f, halfSize.y));
            outline.SetPosition(3, new Vector3(-halfSize.x, 0f, halfSize.y));

            var color = Time.time < target.highlightUntil ? k_HighlightColor : k_RecognizedColor;
            outline.startColor = color;
            outline.endColor = color;
        }
    }
}
