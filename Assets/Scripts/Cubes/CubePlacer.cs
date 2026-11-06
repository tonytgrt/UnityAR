using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// Places a cube wherever the user pinches, and keeps track of the placed cubes so they can be saved with the
    /// room.
    /// </summary>
    /// <remarks>
    /// Cubes are placed under the XR Origin's trackables parent, like the room mesh, so they stay fixed in the room
    /// when the world origin moves. New cubes are centered on the pinch and aligned with the world axes.
    /// </remarks>
    public class CubePlacer : MonoBehaviour
    {
        [SerializeField]
        XROrigin m_XROrigin;

        [SerializeField]
        HandPinch m_HandPinch;

        [SerializeField]
        StatusToast m_Status;

        [SerializeField]
        Material m_CubeMaterial;

        [SerializeField]
        [Tooltip("Edge length of new cubes, in meters.")]
        float m_CubeSize = 0.05f;

        readonly List<Transform> m_Cubes = new List<Transform>();

        /// <summary>
        /// The placed cubes, oldest first.
        /// </summary>
        public IReadOnlyList<Transform> cubes => m_Cubes;

        void OnEnable() => m_HandPinch.pinched += OnPinched;

        void OnDisable() => m_HandPinch.pinched -= OnPinched;

        void OnPinched(Vector3 point)
        {
            Place(new Pose(point, Quaternion.identity), m_CubeSize);
            Debug.Log($"[CubePlacer] Placed cube {m_Cubes.Count} at {point:F3}");
            m_Status.Show($"Cube {m_Cubes.Count} placed. Press Save to image to save it with the room.");
        }

        /// <summary>
        /// Places a cube.
        /// </summary>
        /// <param name="worldPose">The cube's center and rotation in world space.</param>
        /// <param name="size">The length of the cube's edges, in meters.</param>
        public void Place(Pose worldPose, float size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Cube {m_Cubes.Count + 1}";
            Destroy(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = m_CubeMaterial;

            var cubeTransform = cube.transform;
            cubeTransform.SetParent(m_XROrigin.TrackablesParent, false);
            cubeTransform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            cubeTransform.localScale = Vector3.one * size;
            m_Cubes.Add(cubeTransform);
        }

        /// <summary>
        /// Removes all placed cubes.
        /// </summary>
        public void Clear()
        {
            foreach (var cube in m_Cubes)
            {
                if (cube != null)
                    Destroy(cube.gameObject);
            }
            m_Cubes.Clear();
        }
    }
}
