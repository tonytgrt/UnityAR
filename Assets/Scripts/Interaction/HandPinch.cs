using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace UnityAR
{
    /// <summary>
    /// Detects pinches of tracked hands and follows each pinch while it is held.
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
            public bool reported;
            public Vector3 point;
        }

        const int k_CursorPointCount = 24;
        const float k_CursorWidth = 0.002f;
        const float k_MinCursorRadius = 0.006f;
        const float k_MaxCursorRadius = 0.03f;

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
        public event Action<Handedness, Vector3> pinchStarted;

        /// <summary>
        /// Raised when a pinch reported by <see cref="pinchStarted"/> ends, including when the hand stops being tracked.
        /// </summary>
        public event Action<Handedness> pinchEnded;

        /// <summary>
        /// Gets the current pinch point of a hand, if that hand is pinching.
        /// </summary>
        public bool TryGetPinchPoint(Handedness handedness, out Vector3 point)
        {
            var state = handedness == Handedness.Left ? m_LeftHand : m_RightHand;
            point = state.point;
            return state.reported;
        }

        void Awake()
        {
            m_LeftHand = CreateHandState(Handedness.Left);
            m_RightHand = CreateHandState(Handedness.Right);
        }

        void Update()
        {
            if (m_Subsystem == null || !m_Subsystem.running)
                m_Subsystem = HandJoints.FindRunningSubsystem();

            if (m_Subsystem == null)
            {
                Untrack(m_LeftHand);
                Untrack(m_RightHand);
                return;
            }

            UpdateHand(m_LeftHand, m_Subsystem.leftHand);
            UpdateHand(m_RightHand, m_Subsystem.rightHand);
        }

        void UpdateHand(HandState state, XRHand hand)
        {
            if (!HandJoints.TryGetWorldPosition(m_XROrigin, hand, XRHandJointID.ThumbTip, out var thumbTip) ||
                !HandJoints.TryGetWorldPosition(m_XROrigin, hand, XRHandJointID.IndexTip, out var indexTip))
            {
                Untrack(state);
                return;
            }

            if (state.trackedSince < 0f)
                state.trackedSince = Time.time;

            var distance = Vector3.Distance(thumbTip, indexTip);
            state.point = (thumbTip + indexTip) * 0.5f;
            if (state.pinching)
            {
                if (distance > m_ReleaseDistance)
                    EndPinch(state);
            }
            else if (distance < m_PinchDistance)
            {
                // A hand that comes into view already pinching has to open before its next pinch counts.
                state.pinching = true;
                if (Time.time - state.trackedSince >= m_SettleTime)
                {
                    state.reported = true;
                    Debug.Log($"[HandPinch] {state.handedness} hand pinched at {state.point:F3}");
                    pinchStarted?.Invoke(state.handedness, state.point);
                }
            }

            UpdateCursor(state, distance);
        }

        void EndPinch(HandState state)
        {
            var reported = state.reported;
            state.pinching = false;
            state.reported = false;
            if (reported)
                pinchEnded?.Invoke(state.handedness);
        }

        void Untrack(HandState state)
        {
            state.trackedSince = -1f;
            state.cursor.enabled = false;
            EndPinch(state);
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

        void UpdateCursor(HandState state, float fingertipDistance)
        {
            // A ring facing the viewer, around the fingertips until they are close together.
            var radius = Mathf.Clamp(fingertipDistance * 0.5f, k_MinCursorRadius, k_MaxCursorRadius);
            var facing = Quaternion.LookRotation(state.point - m_XROrigin.Camera.transform.position);
            var cursor = state.cursor;
            for (var i = 0; i < k_CursorPointCount; i++)
            {
                var angle = i * Mathf.PI * 2f / k_CursorPointCount;
                cursor.SetPosition(i, state.point + facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }

            var color = state.pinching ? m_PinchColor : m_OpenColor;
            cursor.startColor = color;
            cursor.endColor = color;
            cursor.enabled = true;
        }
    }
}
