using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace UnityAR
{
    /// <summary>
    /// Detects pinches of tracked hands and reports where in the room each pinch happened.
    /// </summary>
    /// <remarks>
    /// A pinch is the tips of the thumb and index finger coming together, and its point is halfway between them.
    /// Each tracked hand shows a ring at that point, which closes in with the fingertips and turns green while
    /// pinching, so the user can see where a pinch will land.
    /// </remarks>
    public class HandPinch : MonoBehaviour
    {
        class HandState
        {
            public Handedness handedness;
            public LineRenderer cursor;
            public float trackedSince = -1f;
            public bool pinching;
        }

        const int k_CursorPointCount = 24;
        const float k_CursorWidth = 0.002f;
        const float k_MinCursorRadius = 0.006f;
        const float k_MaxCursorRadius = 0.03f;

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        [Tooltip("Material for the cursor ring. Must use vertex colors.")]
        Material m_CursorMaterial;

        [SerializeField]
        [Tooltip("Fingertip distance, in meters, below which a hand is pinching.")]
        float m_PinchDistance = 0.025f;

        [SerializeField]
        [Tooltip("Fingertip distance, in meters, above which a pinch ends. Larger than the pinch distance so tracking jitter doesn't end a pinch early or start a second one.")]
        float m_ReleaseDistance = 0.05f;

        [SerializeField]
        [Tooltip("Seconds a hand must be tracked before its pinches count. Hands often come into view with the fingers already together.")]
        float m_SettleTime = 0.3f;

        [SerializeField]
        Color m_OpenColor = new Color(0.85f, 0.85f, 0.85f);

        [SerializeField]
        Color m_PinchColor = new Color(0.35f, 1f, 0.35f);

        XRHandSubsystem m_Subsystem;
        HandState m_LeftHand;
        HandState m_RightHand;

        /// <summary>
        /// Raised when a hand starts pinching, with the world-space point between its thumb and index fingertips.
        /// </summary>
        public event Action<Vector3> pinched;

        void Awake()
        {
            m_LeftHand = CreateHandState(Handedness.Left);
            m_RightHand = CreateHandState(Handedness.Right);
        }

        void Update()
        {
            if (m_Subsystem == null || !m_Subsystem.running)
                m_Subsystem = FindRunningSubsystem();

            if (m_Subsystem == null)
            {
                Untrack(m_LeftHand);
                Untrack(m_RightHand);
                return;
            }

            UpdateHand(m_LeftHand, m_Subsystem.leftHand);
            UpdateHand(m_RightHand, m_Subsystem.rightHand);
        }

        static XRHandSubsystem FindRunningSubsystem()
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem.running)
                    return subsystem;
            }
            return null;
        }

        void UpdateHand(HandState state, XRHand hand)
        {
            if (!TryGetFingertips(hand, out var thumbTip, out var indexTip))
            {
                Untrack(state);
                return;
            }

            if (state.trackedSince < 0f)
                state.trackedSince = Time.time;

            var distance = Vector3.Distance(thumbTip, indexTip);
            var point = (thumbTip + indexTip) * 0.5f;
            if (state.pinching)
            {
                if (distance > m_ReleaseDistance)
                    state.pinching = false;
            }
            else if (distance < m_PinchDistance)
            {
                // A hand that comes into view already pinching has to open before its next pinch counts.
                state.pinching = true;
                if (Time.time - state.trackedSince >= m_SettleTime)
                {
                    Debug.Log($"[HandPinch] {state.handedness} hand pinched at {point:F3}");
                    pinched?.Invoke(point);
                }
            }

            UpdateCursor(state, point, distance);
        }

        bool TryGetFingertips(XRHand hand, out Vector3 thumbTip, out Vector3 indexTip)
        {
            thumbTip = default;
            indexTip = default;
            if (!hand.isTracked ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose))
                return false;

            // Joint poses are in the XR Origin's tracking space, the same space as the camera's pose.
            var trackingSpace = m_XROrigin.CameraFloorOffsetObject.transform;
            thumbTip = trackingSpace.TransformPoint(thumbPose.position);
            indexTip = trackingSpace.TransformPoint(indexPose.position);
            return true;
        }

        static void Untrack(HandState state)
        {
            state.trackedSince = -1f;
            state.pinching = false;
            state.cursor.enabled = false;
        }

        HandState CreateHandState(Handedness handedness)
        {
            var cursorObject = new GameObject($"{handedness} Pinch Cursor");
            cursorObject.transform.SetParent(transform, false);

            var cursor = cursorObject.AddComponent<LineRenderer>();
            cursor.useWorldSpace = true;
            cursor.loop = true;
            cursor.positionCount = k_CursorPointCount;
            cursor.widthMultiplier = k_CursorWidth;
            cursor.sharedMaterial = m_CursorMaterial;
            cursor.shadowCastingMode = ShadowCastingMode.Off;
            cursor.receiveShadows = false;
            cursor.enabled = false;
            return new HandState { handedness = handedness, cursor = cursor };
        }

        void UpdateCursor(HandState state, Vector3 point, float fingertipDistance)
        {
            // A ring facing the viewer, around the fingertips until they are close together.
            var radius = Mathf.Clamp(fingertipDistance * 0.5f, k_MinCursorRadius, k_MaxCursorRadius);
            var facing = Quaternion.LookRotation(point - m_XROrigin.Camera.transform.position);
            var cursor = state.cursor;
            for (var i = 0; i < k_CursorPointCount; i++)
            {
                var angle = i * Mathf.PI * 2f / k_CursorPointCount;
                cursor.SetPosition(i, point + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }

            var color = state.pinching ? m_PinchColor : m_OpenColor;
            cursor.startColor = color;
            cursor.endColor = color;
            cursor.enabled = true;
        }
    }
}
