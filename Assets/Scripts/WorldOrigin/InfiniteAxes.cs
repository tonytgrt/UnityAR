using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAR
{
    /// <summary>
    /// Draws the X (red), Y (green) and Z (blue) axes through this transform's position, extending far
    /// enough in both directions to look endless. The negative half of each axis is dimmer.
    /// </summary>
    public class InfiniteAxes : MonoBehaviour
    {
        // Line points are spaced geometrically from the origin so the camera-facing line width stays
        // accurate close to the viewer while still reaching far away.
        const float k_FirstPointDistance = 0.25f;

        [SerializeField]
        [Tooltip("Unlit material that uses vertex colors, such as Universal Render Pipeline/Particles/Unlit.")]
        Material m_LineMaterial;

        [SerializeField]
        [Tooltip("How far each half axis extends, in meters. Keep it below the camera's far clip plane.")]
        float m_Length = 500f;

        [SerializeField]
        [Tooltip("Line width in meters.")]
        float m_Width = 0.004f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Brightness of the negative half of each axis relative to the positive half.")]
        float m_NegativeBrightness = 0.35f;

        [SerializeField]
        Color m_XColor = new Color(1f, 0.25f, 0.25f);

        [SerializeField]
        Color m_YColor = new Color(0.3f, 1f, 0.3f);

        [SerializeField]
        Color m_ZColor = new Color(0.3f, 0.5f, 1f);

        void Awake()
        {
            CreateAxis("X", Vector3.right, m_XColor);
            CreateAxis("Y", Vector3.up, m_YColor);
            CreateAxis("Z", Vector3.forward, m_ZColor);
        }

        void CreateAxis(string axisName, Vector3 direction, Color color)
        {
            var dimmed = color * m_NegativeBrightness;
            dimmed.a = color.a;
            CreateHalfAxis("+" + axisName, direction, color);
            CreateHalfAxis("-" + axisName, -direction, dimmed);
        }

        void CreateHalfAxis(string lineName, Vector3 direction, Color color)
        {
            var points = new List<Vector3> { Vector3.zero };
            for (var distance = k_FirstPointDistance; distance < m_Length; distance *= 2f)
                points.Add(direction * distance);
            points.Add(direction * m_Length);

            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = m_LineMaterial;
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = m_Width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
        }

        void OnDrawGizmos()
        {
            const float gizmoLength = 1f;
            var origin = transform.position;
            Gizmos.color = m_XColor;
            Gizmos.DrawLine(origin - transform.right * gizmoLength, origin + transform.right * gizmoLength);
            Gizmos.color = m_YColor;
            Gizmos.DrawLine(origin - transform.up * gizmoLength, origin + transform.up * gizmoLength);
            Gizmos.color = m_ZColor;
            Gizmos.DrawLine(origin - transform.forward * gizmoLength, origin + transform.forward * gizmoLength);
        }
    }
}
