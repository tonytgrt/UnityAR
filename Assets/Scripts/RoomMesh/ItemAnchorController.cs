using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace UnityAR
{
    /// <summary>
    /// Tags real-world items with persistent XREAL anchors, each saved together with the room mesh and the world
    /// origin, and restores both when a tagged item is recognized in a later session.
    /// </summary>
    /// <remarks>
    /// <para>Tapping with the beam on a detected surface makes that point the world origin and tags the item there.
    /// XREAL then maps the item and its surroundings while the user looks at it from a few angles. Once enough
    /// angles are mapped well, the anchor is saved together with the current room mesh and the origin, replacing
    /// any item saved at the same spot before. Tapping somewhere else restarts tagging there.</para>
    /// <para>Pressing the phone controller's APP button shows a capture frame in the center of view and starts
    /// looking for saved items. When XREAL recognizes one inside the frame, its room mesh replaces live scanning
    /// and the world origin returns to where it was saved. Pressing APP again stops looking, and pressing it while
    /// a saved mesh is shown resumes live scanning.</para>
    /// <para>Items are recognized by how they and their surroundings look in place, so an item moved somewhere
    /// else is not recognized there.</para>
    /// </remarks>
    public class ItemAnchorController : MonoBehaviour
    {
        class TaggedItem
        {
            public string key;
            public SerializableGuid persistentId;
            public ARAnchor anchor;
            public Pose loadedPose;
            public TrackingState lastTrackingState;
            public bool wasNotTracking;
            public bool located;
        }

        const int k_DirectionCount = 24;
        const float k_DirectionStep = 360f / k_DirectionCount;
        const float k_EstimateInterval = 0.5f;
        const float k_SessionWaitTimeout = 10f;
        const float k_HintInterval = 4f;
        const int k_MaxSaveAttempts = 3;

        // Each failed save asks XREAL to map this many more directions well before trying again.
        const int k_ExtraDirectionsPerRetry = 2;

        // A saved item located this close to a newly saved one is the same item tagged again, and is replaced.
        const float k_SameItemDistance = 0.3f;

        // A loaded anchor that moves this far from where it was loaded has been placed by XREAL recognizing it.
        const float k_LocatedMoveDistance = 0.02f;
        const float k_LocatedTurnAngle = 2f;

        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        ARAnchorManager m_AnchorManager;

        [SerializeField]
        WorldOrigin m_WorldOrigin;

        [SerializeField]
        RoomMeshStore m_Store;

        [SerializeField]
        StatusToast m_Status;

        [SerializeField]
        AnchorMappingGuide m_MappingGuide;

        [SerializeField]
        CaptureFrame m_CaptureFrame;

        [SerializeField]
        [Tooltip("Interactors whose beam can tag items. Leave empty to use every Near-Far Interactor under the XR Origin.")]
        List<NearFarInteractor> m_Interactors = new List<NearFarInteractor>();

        [SerializeField]
        [Range(1, k_DirectionCount)]
        [Tooltip("Number of viewing directions, 15 degrees apart, that XREAL must rate as good before a tagged item is saved.")]
        int m_RequiredGoodDirections = 3;

        [SerializeField]
        [Tooltip("Seconds to map a tagged item before saving what was mapped, or giving up if no direction was mapped well.")]
        float m_MappingTimeout = 60f;

        readonly List<TaggedItem> m_Items = new List<TaggedItem>();
        readonly XREALAnchorEstimateQuality[] m_DirectionQuality = new XREALAnchorEstimateQuality[k_DirectionCount];
        XREALVirtualController m_VirtualController;
        ARAnchor m_MappingAnchor;
        Vector3 m_MappingReference;
        float m_MappingStartTime;
        float m_NextEstimateTime;
        int m_RequiredGoodNow;
        int m_SaveAttempts;
        bool m_Saving;
        bool m_Capturing;
        bool m_SavedItemsLoaded;
        bool m_LoadingSavedItems;
        float m_NextHintTime;

        void Start()
        {
            if (m_Interactors.Count == 0)
                m_Interactors.AddRange(m_XROrigin.GetComponentsInChildren<NearFarInteractor>(true));

            m_Status.Show("Scan the room by looking around, then tap an item to make it the origin and save the room with it. In a later session, press APP and look at the item through the frame.");
        }

        void OnDestroy()
        {
            if (m_VirtualController != null)
                m_VirtualController.pointerDown -= OnVirtualControllerButtonDown;
        }

        void Update()
        {
            // XREAL creates the phone controller when its XR loader starts, so subscribe once it exists.
            if (m_VirtualController == null && XREALVirtualController.Singleton != null)
            {
                m_VirtualController = XREALVirtualController.Singleton;
                m_VirtualController.pointerDown += OnVirtualControllerButtonDown;
            }

            HandleTaps();

            foreach (var item in m_Items)
                UpdateLocated(item);

            if (m_Capturing)
                UpdateCapture();

            if (m_MappingAnchor != null && !m_Saving)
                UpdateMapping();
        }

        void HandleTaps()
        {
            foreach (var interactor in m_Interactors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled || !WasTapped(interactor))
                    continue;

                if (BeamRaycast.TryGetDetectedSurfaceHit(interactor, m_XROrigin, out var hit))
                    OnTap(hit.point);
                else
                    Debug.Log("[ItemAnchor] Tap ignored: beam is not on a detected surface");
                return;
            }
        }

        // The phone controller's tap is its trigger, which the XRI default input actions bind to Activate; a
        // tracked hand's pinch is Select.
        static bool WasTapped(NearFarInteractor interactor)
        {
            return interactor.activateInput.ReadWasPerformedThisFrame() ||
                interactor.selectInput.ReadWasPerformedThisFrame();
        }

        void OnTap(Vector3 point)
        {
            if (m_Saving)
            {
                m_Status.Show("Still saving the tagged item...");
                return;
            }

            if (m_Capturing)
                StopCapture(null);
            if (m_MappingAnchor != null)
                CancelMapping(null);

            // Tag before moving the origin: the anchor lives under the XR Origin, so it moves along with it.
            var hasMesh = m_Store.currentTriangleCount > 0;
            if (hasMesh)
                StartMapping(point);
            m_WorldOrigin.SetOrigin(point);
            if (!hasMesh)
                m_Status.Show("Origin set. To save it with the room, scan the room first, then tap the item again.");
        }

        void OnVirtualControllerButtonDown(XREALButtonType button, GameObject buttonObject, PointerEventData eventData)
        {
            if (button == XREALButtonType.CustomButton0)
                OnAppButton();
        }

        void OnAppButton()
        {
            if (m_MappingAnchor != null || m_Saving)
            {
                m_Status.Show("Finish tagging the item first, or tap somewhere else to start over.");
                return;
            }

            if (!m_Store.isLiveScanning)
            {
                m_Store.ResumeLiveScanning();
                m_Status.Show("Rescanning the room. Tap an item to save the room with it, or press APP to look for a tagged item.");
                return;
            }

            if (m_Capturing)
                StopCapture("Stopped looking for tagged items.");
            else
                StartCapture();
        }

        async void StartCapture()
        {
            m_Capturing = true;
            m_CaptureFrame.Show();
            m_NextHintTime = Time.time + k_HintInterval;
            m_Status.Show("Looking for tagged items. Look at one through the frame.");

            if (m_SavedItemsLoaded || m_LoadingSavedItems)
                return;

            await LoadSavedItems();
            if (m_Capturing && m_Items.Count == 0)
                StopCapture("No tagged items are saved yet. Tap an item with the beam to tag it.");
        }

        void StopCapture(string message)
        {
            m_Capturing = false;
            m_CaptureFrame.Hide();
            if (message != null)
                m_Status.Show(message);
        }

        async Awaitable LoadSavedItems()
        {
            m_LoadingSavedItems = true;
            try
            {
                // XREAL can only load anchors once its session is tracking.
                var deadline = Time.time + k_SessionWaitTimeout;
                while (ARSession.state != ARSessionState.SessionTracking && Time.time < deadline)
                    await Awaitable.NextFrameAsync();

                if (m_AnchorManager.subsystem == null)
                {
                    Debug.LogWarning("[ItemAnchor] No anchor subsystem is running, so tagged items cannot be loaded.");
                    return;
                }

                var idsResult = await m_AnchorManager.TryGetSavedAnchorIdsAsync(Allocator.Persistent);

                // XREAL completes this on a worker thread.
                await Awaitable.MainThreadAsync();
                if (!idsResult.status.IsSuccess())
                {
                    Debug.LogWarning($"[ItemAnchor] Could not list saved anchors: {idsResult.status}");
                    return;
                }

                var savedIds = idsResult.value;
                try
                {
                    foreach (var savedId in savedIds)
                    {
                        var key = GetAnchorKey(savedId);
                        if (!m_Store.HasSavedMesh(key) || m_Items.Exists(item => item.key == key))
                            continue;

                        var loadResult = await m_AnchorManager.TryLoadAnchorAsync(savedId);
                        if (!loadResult.status.IsSuccess() || loadResult.value == null)
                        {
                            Debug.LogWarning($"[ItemAnchor] Could not load anchor {key}: {loadResult.status}");
                            continue;
                        }

                        var anchor = loadResult.value;
                        var item = new TaggedItem
                        {
                            key = key,
                            persistentId = savedId,
                            anchor = anchor,
                            loadedPose = GetSessionPose(anchor.transform),
                            lastTrackingState = anchor.trackingState,
                        };
                        m_Items.Add(item);
                        Debug.Log($"[ItemAnchor] Loaded anchor {key}: trackingState={anchor.trackingState}, sessionPose={item.loadedPose.position:F3}");
                    }
                }
                finally
                {
                    savedIds.Dispose();
                }

                m_SavedItemsLoaded = true;
                Debug.Log($"[ItemAnchor] {m_Items.Count} tagged item(s) loaded");
            }
            finally
            {
                m_LoadingSavedItems = false;
            }
        }

        void UpdateLocated(TaggedItem item)
        {
            if (item.located || item.anchor == null)
                return;

            var trackingState = item.anchor.trackingState;
            if (trackingState != item.lastTrackingState)
            {
                Debug.Log($"[ItemAnchor] Anchor {item.key} tracking state {item.lastTrackingState} -> {trackingState}");
                item.lastTrackingState = trackingState;
            }

            if (trackingState != TrackingState.Tracking)
            {
                item.wasNotTracking = true;
                return;
            }

            // Once XREAL recognizes the surroundings it reports where the anchor really is, which moves it away from
            // where it was loaded, or the anchor goes from not tracking to tracking.
            var pose = GetSessionPose(item.anchor.transform);
            var moved = Vector3.Distance(pose.position, item.loadedPose.position) > k_LocatedMoveDistance ||
                Quaternion.Angle(pose.rotation, item.loadedPose.rotation) > k_LocatedTurnAngle;
            if (!moved && !item.wasNotTracking)
                return;

            item.located = true;
            Debug.Log($"[ItemAnchor] Located item {item.key} at {pose.position:F3} (session space); moved={moved}, wasNotTracking={item.wasNotTracking}");
        }

        void UpdateCapture()
        {
            // Prefer the most recently saved item if several are in the frame.
            TaggedItem found = null;
            var foundOutsideFrame = false;
            foreach (var item in m_Items)
            {
                if (!item.located || item.anchor == null)
                    continue;

                if (!m_CaptureFrame.Contains(item.anchor.transform.position))
                {
                    foundOutsideFrame = true;
                    continue;
                }

                if (found == null || m_Store.GetSaveTime(item.key) > m_Store.GetSaveTime(found.key))
                    found = item;
            }

            if (found != null)
            {
                Restore(found);
                return;
            }

            if (Time.time >= m_NextHintTime)
            {
                m_NextHintTime = Time.time + k_HintInterval;
                m_Status.Show(foundOutsideFrame
                    ? "Found a tagged item. Center it in the frame."
                    : "Looking for tagged items. Look at one through the frame, from about where you tagged it.");
            }
        }

        void Restore(TaggedItem item)
        {
            try
            {
                var anchor = item.anchor.transform;
                var data = m_Store.Load(item.key, anchor);
                m_WorldOrigin.SetOrigin(new Pose(
                    anchor.TransformPoint(data.originPose.position),
                    anchor.rotation * data.originPose.rotation));
                Debug.Log($"[ItemAnchor] Restored item {item.key}: {data.triangleCount} triangles");
                StopCapture($"Recognized the tagged item. Restored the room and origin saved {data.savedAtUtc.ToLocalTime():g}. Press APP to rescan.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                StopCapture($"Could not restore this item's room: {exception.Message}");
            }
        }

        void StartMapping(Vector3 point)
        {
            // Face the anchor toward the user, level with the ground.
            var toUser = m_XROrigin.Camera.transform.position - point;
            toUser.y = 0f;
            if (toUser.sqrMagnitude < 1e-6f)
                toUser = Vector3.ProjectOnPlane(-m_XROrigin.Camera.transform.forward, Vector3.up);
            m_MappingReference = toUser.normalized;

            // XREAL's asynchronous add completes off the main thread, which AR Foundation cannot handle, so add the
            // anchor synchronously by attaching an ARAnchor component, as XREAL's own sample does.
            var anchorObject = new GameObject("Tagged Item");
            anchorObject.transform.SetParent(m_XROrigin.TrackablesParent, false);
            anchorObject.transform.SetPositionAndRotation(point, Quaternion.LookRotation(m_MappingReference, Vector3.up));
            m_MappingAnchor = anchorObject.AddComponent<ARAnchor>();

            m_MappingStartTime = Time.time;
            m_NextEstimateTime = Time.time + k_EstimateInterval;
            m_RequiredGoodNow = m_RequiredGoodDirections;
            m_SaveAttempts = 0;
            Array.Clear(m_DirectionQuality, 0, m_DirectionQuality.Length);

            // Center the first segment on the user's direction.
            var firstSegmentStart = Quaternion.AngleAxis(-k_DirectionStep * 0.5f, Vector3.up) * m_MappingReference;
            m_MappingGuide.Show(anchorObject.transform, firstSegmentStart, k_DirectionCount);
            Debug.Log($"[ItemAnchor] Tagged item at {point:F3}; mapping");
            m_Status.Show($"Origin set and item tagged. Now look at the item from a few different angles so XREAL can map it (0/{m_RequiredGoodNow}).");
        }

        void UpdateMapping()
        {
            var anchorTransform = m_MappingAnchor.transform;
            var camera = m_XROrigin.Camera.transform;
            var direction = GetDirectionIndex(anchorTransform.position, camera.position);
            m_MappingGuide.SetCurrent(direction);

            if (Time.time >= m_NextEstimateTime && m_MappingAnchor.trackingState == TrackingState.Tracking)
            {
                m_NextEstimateTime = Time.time + k_EstimateInterval;

                // XREAL expects the viewpoint in session space, which differs from world space once the world origin
                // has moved.
                var quality = m_AnchorManager.GetAnchorQuality(m_MappingAnchor.trackableId, GetSessionPose(camera));
                if (quality > m_DirectionQuality[direction])
                {
                    m_DirectionQuality[direction] = quality;
                    m_MappingGuide.SetQuality(direction, quality);
                    var good = CountGoodDirections();
                    if (quality == XREALAnchorEstimateQuality.XREAL_ANCHOR_ESTIMATE_QUALITY_GOOD && good < m_RequiredGoodNow)
                        m_Status.Show($"Mapping the item ({good}/{m_RequiredGoodNow}). Keep looking at it from other angles.");
                }
            }

            if (CountGoodDirections() >= m_RequiredGoodNow || Time.time - m_MappingStartTime >= m_MappingTimeout)
                FinishMapping();
        }

        void FinishMapping()
        {
            if (CountGoodDirections() == 0)
            {
                CancelMapping("Could not map this item well enough. Try one with more visible detail or better light, and look at it from closer.");
                return;
            }
            SaveMappedItem();
        }

        async void SaveMappedItem()
        {
            m_Saving = true;
            var anchor = m_MappingAnchor;
            m_Status.Show("Saving the tagged item...");
            try
            {
                var result = await m_AnchorManager.TrySaveAnchorAsync(anchor);

                // XREAL completes saves on a worker thread.
                await Awaitable.MainThreadAsync();
                if (anchor == null || anchor != m_MappingAnchor)
                    return;

                if (result.status.statusCode != XRResultStatus.StatusCode.UnqualifiedSuccess)
                {
                    RetryOrCancelMapping(result.status.statusCode);
                    return;
                }

                var item = new TaggedItem
                {
                    key = GetAnchorKey(result.value),
                    persistentId = result.value,
                    anchor = anchor,
                    located = true,
                };
                var data = m_Store.Save(item.key, anchor.transform);
                m_Items.Add(item);
                m_MappingGuide.Hide();
                m_MappingAnchor = null;
                Debug.Log($"[ItemAnchor] Saved item {item.key} with {data.triangleCount} triangles");
                m_Status.Show($"Saved the room ({data.triangleCount:N0} triangles) and origin with this item. In a later session, press APP and look at it through the frame.");
                ForgetReplacedItems(item);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                CancelMapping($"Could not save this item: {exception.Message}");
            }
            finally
            {
                m_Saving = false;
            }
        }

        void RetryOrCancelMapping(XRResultStatus.StatusCode statusCode)
        {
            m_SaveAttempts++;
            Debug.LogWarning($"[ItemAnchor] XREAL could not save the anchor ({statusCode}), attempt {m_SaveAttempts} of {k_MaxSaveAttempts}");
            if (m_SaveAttempts >= k_MaxSaveAttempts)
            {
                CancelMapping("XREAL could not save this item. Try one with more visible detail or better light.");
                return;
            }

            // Keep the anchor and map more of the surroundings before trying again.
            m_RequiredGoodNow = Mathf.Min(k_DirectionCount, m_RequiredGoodNow + k_ExtraDirectionsPerRetry);
            m_MappingStartTime = Time.time;
            m_Status.Show($"XREAL needs more of the item mapped. Keep looking at it from other angles ({CountGoodDirections()}/{m_RequiredGoodNow}).");
        }

        // Saved items located at the same spot as a newly saved one are the same item tagged again. Their saved data
        // is erased, but their anchors stay in this session because a loaded mesh may be attached to them.
        async void ForgetReplacedItems(TaggedItem newItem)
        {
            foreach (var item in m_Items.ToArray())
            {
                if (item == newItem || !item.located || item.anchor == null ||
                    Vector3.Distance(item.anchor.transform.position, newItem.anchor.transform.position) > k_SameItemDistance)
                {
                    continue;
                }

                m_Items.Remove(item);
                m_Store.Delete(item.key);
                var status = await m_AnchorManager.TryEraseAnchorAsync(item.persistentId);

                // XREAL completes this on a worker thread.
                await Awaitable.MainThreadAsync();
                Debug.Log($"[ItemAnchor] Replaced item {item.key} with {newItem.key} (erase: {status})");
            }
        }

        void CancelMapping(string message)
        {
            m_MappingGuide.Hide();
            if (m_MappingAnchor != null)
                Destroy(m_MappingAnchor.gameObject);
            m_MappingAnchor = null;
            if (message != null)
                m_Status.Show(message);
        }

        int GetDirectionIndex(Vector3 anchorPosition, Vector3 viewerPosition)
        {
            var toViewer = viewerPosition - anchorPosition;
            toViewer.y = 0f;
            var angle = Vector3.SignedAngle(m_MappingReference, toViewer, Vector3.up);
            return Mathf.FloorToInt(Mathf.Repeat(angle + k_DirectionStep * 0.5f, 360f) / k_DirectionStep) % k_DirectionCount;
        }

        int CountGoodDirections()
        {
            var count = 0;
            foreach (var quality in m_DirectionQuality)
            {
                if (quality == XREALAnchorEstimateQuality.XREAL_ANCHOR_ESTIMATE_QUALITY_GOOD)
                    count++;
            }
            return count;
        }

        Pose GetSessionPose(Transform target)
        {
            var trackables = m_XROrigin.TrackablesParent;
            return new Pose(
                trackables.InverseTransformPoint(target.position),
                Quaternion.Inverse(trackables.rotation) * target.rotation);
        }

        // XREAL reports the same anchor ID with different byte orders when saving it and when listing saved anchors,
        // so files are keyed by whichever of the two readings sorts first, which is the same for both.
        static string GetAnchorKey(SerializableGuid id)
        {
            var guid = id.guid;
            var formatted = guid.ToString("N");
            var raw = new StringBuilder(32);
            foreach (var value in guid.ToByteArray())
                raw.Append(value.ToString("x2"));
            var rawString = raw.ToString();
            return string.CompareOrdinal(formatted, rawString) <= 0 ? formatted : rawString;
        }
    }
}
