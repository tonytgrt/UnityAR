using Unity.XR.CoreUtils;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// Keeps a chosen physical point at Unity world position (0, 0, 0).
    /// </summary>
    /// <remarks>
    /// The XR Origin is shifted rather than the content: the camera, controllers and detected planes all
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
        /// Makes the physical point currently at <paramref name="worldPoint"/> the new world origin.
        /// </summary>
        /// <param name="worldPoint">The point in current world space that becomes (0, 0, 0).</param>
        public void SetOrigin(Vector3 worldPoint)
        {
            m_XROrigin.Origin.transform.position -= worldPoint;

            // The XR Origin carries a CharacterController, so push the move to physics immediately.
            Physics.SyncTransforms();

            Debug.Log($"[WorldOrigin] Moved world origin by {worldPoint:F3}; XR Origin now at {m_XROrigin.Origin.transform.position:F3}");
        }
    }
}
