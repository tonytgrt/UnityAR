using Unity.XR.XREAL;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAR
{
    /// <summary>
    /// Shows how well XREAL has mapped a tagged item from each viewing direction: a ring of segments around the
    /// item that turn red, yellow or green, with the segment the user is looking from drawn thicker, and a pin
    /// marking the item.
    /// </summary>
    public class AnchorMappingGuide : MonoBehaviour
    {
        const float k_Radius = 0.18f;
        const float k_PinHeight = 0.15f;
        const float k_LineWidth = 0.004f;
        const float k_CurrentLineWidth = 0.01f;
        const int k_PointsPerSegment = 4;
        const float k_SegmentGapDegrees = 2f;

        [SerializeField]
        [Tooltip("Material for the guide lines. Must use vertex colors.")]
        Material m_LineMaterial;

        [SerializeField]
        Color m_UnvisitedColor = new Color(0.35f, 0.35f, 0.35f);

        [SerializeField]
        Color m_InsufficientColor = new Color(1f, 0.3f, 0.25f);

        [SerializeField]
        Color m_SufficientColor = new Color(1f, 0.85f, 0.2f);

        [SerializeField]
        Color m_GoodColor = new Color(0.35f, 1f, 0.35f);

        [SerializeField]
        Color m_PinColor = new Color(0.3f, 0.85f, 1f);

        GameObject m_Root;
        LineRenderer[] m_Segments;
        int m_CurrentSegment = -1;

        /// <summary>
        /// Shows the guide around <paramref name="anchor"/>, with all directions unvisited.
        /// </summary>
        /// <param name="anchor">The tagged item's anchor. The guide is attached to it and follows it.</param>
        /// <param name="referenceDirection">World-space horizontal direction of the first segment's start.</param>
        /// <param name="segmentCount">Number of segments around the full circle.</param>
        public void Show(Transform anchor, Vector3 referenceDirection, int segmentCount)
        {
            Hide();
            m_Root = new GameObject("Anchor Mapping Guide");
            m_Root.transform.SetParent(anchor, false);

            var pin = CreateLine("Pin", 2);
            pin.SetPosition(0, Vector3.zero);
            pin.SetPosition(1, anchor.InverseTransformDirection(Vector3.up) * k_PinHeight);
            SetColor(pin, m_PinColor);

            var step = 360f / segmentCount;
            m_Segments = new LineRenderer[segmentCount];
            for (var i = 0; i < segmentCount; i++)
            {
                var segment = CreateLine($"Segment {i}", k_PointsPerSegment);
                for (var p = 0; p < k_PointsPerSegment; p++)
                {
                    var angle = i * step + k_SegmentGapDegrees +
                        (step - 2f * k_SegmentGapDegrees) * p / (k_PointsPerSegment - 1);
                    var direction = Quaternion.AngleAxis(angle, Vector3.up) * referenceDirection;
                    segment.SetPosition(p, anchor.InverseTransformDirection(direction) * k_Radius);
                }
                SetColor(segment, m_UnvisitedColor);
                m_Segments[i] = segment;
            }
            m_CurrentSegment = -1;
        }

        /// <summary>
        /// Colors the segment for a viewing direction by the quality XREAL reported from it.
        /// </summary>
        public void SetQuality(int segment, XREALAnchorEstimateQuality quality)
        {
            if (m_Segments == null || segment < 0 || segment >= m_Segments.Length)
                return;

            SetColor(m_Segments[segment], quality switch
            {
                XREALAnchorEstimateQuality.XREAL_ANCHOR_ESTIMATE_QUALITY_GOOD => m_GoodColor,
                XREALAnchorEstimateQuality.XREAL_ANCHOR_ESTIMATE_QUALITY_SUFFICIENT => m_SufficientColor,
                _ => m_InsufficientColor,
            });
        }

        /// <summary>
        /// Highlights the segment for the direction the user is currently looking from.
        /// </summary>
        public void SetCurrent(int segment)
        {
            if (m_Segments == null || segment == m_CurrentSegment)
                return;

            if (m_CurrentSegment >= 0 && m_CurrentSegment < m_Segments.Length)
                m_Segments[m_CurrentSegment].widthMultiplier = k_LineWidth;
            if (segment >= 0 && segment < m_Segments.Length)
                m_Segments[segment].widthMultiplier = k_CurrentLineWidth;
            m_CurrentSegment = segment;
        }

        /// <summary>
        /// Removes the guide.
        /// </summary>
        public void Hide()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_Root = null;
            m_Segments = null;
            m_CurrentSegment = -1;
        }

        LineRenderer CreateLine(string lineName, int pointCount)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(m_Root.transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = m_LineMaterial;
            line.widthMultiplier = k_LineWidth;
            line.positionCount = pointCount;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static void SetColor(LineRenderer line, Color color)
        {
            line.startColor = color;
            line.endColor = color;
        }
    }
}
