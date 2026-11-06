using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace UnityAR
{
    /// <summary>
    /// Places and moves cubes by pinching, and keeps track of the placed cubes so they can be saved with the room.
    /// </summary>
    /// <remarks>
    /// <para>Pinching on or next to a cube picks it up, and pinching anywhere else places a new cube centered on the
    /// pinch and picks that up. A picked-up cube follows the pinch until the fingers open, keeping its rotation.</para>
    /// <para>Cubes are placed under the XR Origin's trackables parent, like the room mesh, so they stay fixed in the
    /// room when the world origin moves. New cubes are aligned with the world axes.</para>
    /// </remarks>
    public class CubePlacer : MonoBehaviour
    {
        class Grab
        {
            public Transform cube;
            public Vector3 offset;
        }

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

        [SerializeField]
        [Tooltip("How far outside a cube, in meters, a pinch can start and still pick it up.")]
        float m_GrabMargin = 0.03f;

        readonly List<Transform> m_Cubes = new List<Transform>();
        readonly Dictionary<Handedness, Grab> m_Grabs = new Dictionary<Handedness, Grab>();

        /// <summary>
        /// The placed cubes, oldest first.
        /// </summary>
        public IReadOnlyList<Transform> cubes => m_Cubes;

        void OnEnable()
        {
            m_HandPinch.pinchStarted += OnPinchStarted;
            m_HandPinch.pinchEnded += OnPinchEnded;
        }

        void OnDisable()
        {
            m_HandPinch.pinchStarted -= OnPinchStarted;
            m_HandPinch.pinchEnded -= OnPinchEnded;
            m_Grabs.Clear();
        }

        void Update()
        {
            foreach (var pair in m_Grabs)
            {
                var grab = pair.Value;
                if (grab.cube != null && m_HandPinch.TryGetPinchPoint(pair.Key, out var point))
                    grab.cube.position = point + grab.offset;
            }
        }

        void OnPinchStarted(Handedness hand, Vector3 point)
        {
            var cube = FindCubeToGrab(point);
            if (cube == null)
            {
                cube = Place(new Pose(point, Quaternion.identity), m_CubeSize);
                Debug.Log($"[CubePlacer] Placed cube {m_Cubes.Count} at {point:F3}");
                m_Status.Show($"Cube {m_Cubes.Count} placed. Pinch a cube to move it. Press Save to image to save the cubes with the room.");
            }
            else
            {
                Debug.Log($"[CubePlacer] {hand} hand picked up {cube.name}");
            }

            m_Grabs[hand] = new Grab { cube = cube, offset = cube.position - point };
        }

        void OnPinchEnded(Handedness hand)
        {
            if (!m_Grabs.Remove(hand, out var grab) || grab.cube == null)
                return;
            Debug.Log($"[CubePlacer] {hand} hand put down {grab.cube.name} at {grab.cube.position:F3}");
        }

        // The cube closest to the point, if the point is inside it or within the grab margin of its surface. Cubes
        // already held by the other hand are skipped.
        Transform FindCubeToGrab(Vector3 point)
        {
            Transform closest = null;
            var closestDistance = m_GrabMargin;
            foreach (var cube in m_Cubes)
            {
                if (cube == null || IsHeld(cube))
                    continue;

                var local = Quaternion.Inverse(cube.rotation) * (point - cube.position);
                var halfSize = cube.localScale.x * 0.5f;
                var outside = new Vector3(
                    Mathf.Max(Mathf.Abs(local.x) - halfSize, 0f),
                    Mathf.Max(Mathf.Abs(local.y) - halfSize, 0f),
                    Mathf.Max(Mathf.Abs(local.z) - halfSize, 0f));
                var distance = outside.magnitude;
                if (distance <= closestDistance)
                {
                    closest = cube;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        bool IsHeld(Transform cube)
        {
            foreach (var grab in m_Grabs.Values)
            {
                if (grab.cube == cube)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Places a cube.
        /// </summary>
        /// <param name="worldPose">The cube's center and rotation in world space.</param>
        /// <param name="size">The length of the cube's edges, in meters.</param>
        /// <returns>The new cube.</returns>
        public Transform Place(Pose worldPose, float size)
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
            return cubeTransform;
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
            m_Grabs.Clear();
        }
    }
}
