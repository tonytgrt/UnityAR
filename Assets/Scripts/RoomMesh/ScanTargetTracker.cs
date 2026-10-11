using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityAR
{
    /// <summary>
    /// Recognizes scan targets (reference images) and saves or loads the room mesh that belongs to each one.
    /// </summary>
    /// <remarks>
    /// <para>The first time a target is recognized in a session: if it has a saved room mesh and nothing has
    /// been saved or loaded yet, that mesh is loaded in place of live scanning; if it has no saved mesh, the live
    /// mesh is saved to it.</para>
    /// <para>The APP button on the phone controller saves the live mesh to the recognized target, replacing what
    /// was saved before. While a saved mesh is loaded, it discards the loaded mesh and resumes live scanning.</para>
    /// <para>Meshes are anchored to the target's position and heading, so they line up again wherever the
    /// target is recognized in a later session, as long as the target has not moved in the room.</para>
    /// </remarks>
    public class ScanTargetTracker : MonoBehaviour
    {
        class Target
        {
            public ARTrackedImage image;
            public LineRenderer outline;
            public float trackedSince = -1f;
            public bool hasPose;
            public Vector3 position;
            public Quaternion rotation;
            public bool handled;
            public float highlightUntil;
        }

        // Live meshes smaller than this are treated as not scanned yet, so a target seen right after launch
        // does not get an almost empty mesh saved to it.
        const int k_MinTrianglesToSave = 500;
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
        RoomMeshStore m_Store;

        [SerializeField]
        StatusToast m_Status;

        [SerializeField]
        [Tooltip("Material for the outline drawn on recognized targets. Must use vertex colors.")]
        Material m_OutlineMaterial;

        [SerializeField]
        [Tooltip("How long a target must be tracked continuously before it is used, in seconds.")]
        float m_SettleTime = 0.6f;

        [SerializeField]
        [Tooltip("Anchor meshes to the target's position and heading only, ignoring its tilt. Image tilt " +
            "estimates are noisy, and even a small tilt error swings distant walls noticeably.")]
        bool m_KeepAnchorLevel = true;

        readonly Dictionary<TrackableId, Target> m_Targets = new Dictionary<TrackableId, Target>();
        readonly List<TrackableId> m_RemovedTargets = new List<TrackableId>();
        bool m_AutoLoadAllowed = true;

        void Start()
        {
            XREALVirtualController.Singleton.pointerDown += OnVirtualControllerButtonDown;
        }

        void OnDestroy()
        {
            if (XREALVirtualController.Singleton != null)
                XREALVirtualController.Singleton.pointerDown -= OnVirtualControllerButtonDown;
        }

        void Update()
        {
            foreach (var image in m_TrackedImageManager.trackables)
                UpdateTarget(image);

            m_RemovedTargets.Clear();
            foreach (var pair in m_Targets)
            {
                if (pair.Value.image == null)
                    m_RemovedTargets.Add(pair.Key);
            }
            foreach (var id in m_RemovedTargets)
                m_Targets.Remove(id);
        }

        void OnVirtualControllerButtonDown(XREALButtonType button, GameObject buttonObject, PointerEventData eventData)
        {
            if (button == XREALButtonType.CustomButton0)
                OnAppButton();
        }

        void UpdateTarget(ARTrackedImage image)
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
                return;
            }

            if (target.trackedSince < 0f)
                target.trackedSince = Time.time;

            UpdatePose(target);
            UpdateOutline(target);

            if (!target.handled && Time.time - target.trackedSince >= m_SettleTime)
            {
                target.handled = true;
                OnTargetRecognized(target);
            }
        }

        void OnTargetRecognized(Target target)
        {
            var targetName = GetName(target);
            Debug.Log($"[ScanTarget] Recognized '{targetName}' at {target.position:F3} (session space)");

            var hasSavedMesh = m_Store.HasSavedMesh(targetName);
            if (hasSavedMesh && m_AutoLoadAllowed)
            {
                Load(target);
            }
            else if (!hasSavedMesh && m_Store.isLiveScanning)
            {
                if (m_Store.liveTriangleCount >= k_MinTrianglesToSave)
                    Save(target);
                else
                    m_Status.Show($"Recognized '{targetName}'. Scan the room by looking around, then press APP while looking at it to save.");
            }
            else if (m_Store.isLiveScanning)
            {
                m_Status.Show($"Recognized '{targetName}'. Press APP to save the room mesh to it.");
            }
        }

        void OnAppButton()
        {
            if (!m_Store.isLiveScanning)
            {
                m_Store.ResumeLiveScanning();
                m_AutoLoadAllowed = false;
                m_Status.Show("Rescanning. Look around the room, then press APP while looking at a scan target to save.");
                return;
            }

            var target = GetRecognizedTarget();
            if (target == null)
            {
                m_Status.Show("Look at a scan target, then press APP to save the room mesh to it.");
                return;
            }

            if (m_Store.liveTriangleCount == 0)
            {
                m_Status.Show("Nothing to save yet. Look around the room to scan it first.");
                return;
            }

            Save(target);
        }

        void Save(Target target)
        {
            var targetName = GetName(target);
            try
            {
                var data = m_Store.Save(targetName, GetAnchor(target));
                m_AutoLoadAllowed = false;
                Highlight(target);
                m_Status.Show($"Saved the room mesh to '{targetName}' ({data.triangleCount:N0} triangles).");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Status.Show($"Could not save the room mesh to '{targetName}': {exception.Message}");
            }
        }

        void Load(Target target)
        {
            var targetName = GetName(target);
            try
            {
                var data = m_Store.Load(targetName, GetAnchor(target));
                m_AutoLoadAllowed = false;
                Highlight(target);
                m_Status.Show($"Loaded the room mesh saved to '{targetName}' on {data.savedAtUtc.ToLocalTime():g}. Press APP to rescan.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Status.Show($"Could not load the room mesh for '{targetName}': {exception.Message}");
            }
        }

        // The recognized target closest to the user, which is most likely the one they are looking at.
        Target GetRecognizedTarget()
        {
            Target closest = null;
            var closestDistance = float.MaxValue;
            var cameraPosition = m_XROrigin.Camera.transform.position;
            foreach (var target in m_Targets.Values)
            {
                if (target.image == null || target.image.trackingState != TrackingState.Tracking || !target.hasPose)
                    continue;
                var distance = Vector3.Distance(cameraPosition, target.image.transform.position);
                if (distance < closestDistance)
                {
                    closest = target;
                    closestDistance = distance;
                }
            }
            return closest;
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

        Pose GetAnchor(Target target)
        {
            if (!m_KeepAnchorLevel)
                return new Pose(target.position, target.rotation);

            // Keep only the heading. Use whichever of the image's normal and its in-plane up axis is closer to
            // horizontal, so this works both for images lying flat and for images standing upright.
            var normal = target.rotation * Vector3.up;
            var imageUp = target.rotation * Vector3.forward;
            var heading = Mathf.Abs(normal.y) > Mathf.Abs(imageUp.y) ? imageUp : normal;
            heading.y = 0f;
            return new Pose(target.position, Quaternion.LookRotation(heading.normalized, Vector3.up));
        }

        static string GetName(Target target) => target.image.referenceImage.name;

        void Highlight(Target target) => target.highlightUntil = Time.time + k_HighlightDuration;

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
            outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
