using Unity.XR.CoreUtils;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// Keeps a chosen physical point at Unity world position (0, 0, 0), optionally with a chosen heading as the
    /// world's forward direction.
    /// </summary>
    /// <remarks>
    /// The XR Origin is moved rather than the content: the camera, controllers and detected geometry all
    /// live under it, so they stay consistent with the real world while the world origin moves to the
    /// chosen point. Anything placed at world coordinates is therefore positioned relative to that point.
    /// </remarks>
    public class WorldOrigin : MonoBehaviour
    {
        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        [Tooltip("Where the world origin starts, relative to the XR Origin when the app launches. " +
            "The default is where the HelloMR panel was: 1.5 m ahead, just below eye height.")]
        Vector3 m_InitialOrigin = new Vector3(0f, 1.31f, 1.5f);

        /// <summary>
        /// The XR Origin that is moved to keep the world origin in place.
        /// </summary>
        public XROrigin xrOrigin => m_XROrigin;

        void Start()
        {
            SetOrigin(m_XROrigin.Origin.transform.TransformPoint(m_InitialOrigin));
        }

        /// <summary>
        /// Makes the physical point currently at <paramref name="worldPoint"/> the new world origin, keeping the
        /// directions of the world axes.
        /// </summary>
        /// <param name="worldPoint">The point in current world space that becomes (0, 0, 0).</param>
        public void SetOrigin(Vector3 worldPoint) => SetOrigin(new Pose(worldPoint, Quaternion.identity));

        /// <summary>
        /// Makes the physical pose currently at <paramref name="worldPose"/> the new world origin: its position
        /// becomes (0, 0, 0) and its heading becomes the world's forward (+Z) direction.
        /// </summary>
        /// <remarks>
        /// Only the heading of the rotation is used, so the world stays level with gravity.
        /// </remarks>
        /// <param name="worldPose">The pose in current world space that becomes the origin.</param>
        public void SetOrigin(Pose worldPose)
        {
            var heading = Quaternion.Euler(0f, worldPose.rotation.eulerAngles.y, 0f);
            var toNewWorld = Quaternion.Inverse(heading);
            var origin = m_XROrigin.Origin.transform;
            origin.SetPositionAndRotation(toNewWorld * (origin.position - worldPose.position), toNewWorld * origin.rotation);

            // The XR Origin carries a CharacterController, so push the move to physics immediately.
            Physics.SyncTransforms();

            Debug.Log($"[WorldOrigin] New origin at {worldPose.position:F3} with heading {heading.eulerAngles.y:F1}; XR Origin now at {origin.position:F3}");
        }
    }
}
