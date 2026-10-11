using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAR
{
    /// <summary>
    /// A viewfinder frame fixed in the center of the user's view, used to aim at a tagged item to recognize.
    /// </summary>
    public class CaptureFrame : MonoBehaviour
    {
        const float k_LineWidth = 0.003f;
        const float k_CornerLength = 0.25f;

        [SerializeField]
        [Tooltip("The camera the frame stays in front of.")]
        Camera m_Camera;

        [SerializeField]
        [Tooltip("Material for the frame lines. Must use vertex colors.")]
        Material m_LineMaterial;

        [SerializeField]
        [Tooltip("Width of the frame, in degrees of the field of view.")]
        float m_HorizontalAngle = 20f;

        [SerializeField]
        [Tooltip("Height of the frame, in degrees of the field of view.")]
        float m_VerticalAngle = 15f;

        [SerializeField]
        [Tooltip("Distance of the frame in front of the camera, in meters.")]
        float m_Distance = 1f;

        [SerializeField]
        Color m_Color = Color.white;

        GameObject m_Frame;
        LineRenderer[] m_Corners;

        /// <summary>
        /// Whether the frame is shown.
        /// </summary>
        public bool isShown => m_Frame != null && m_Frame.activeSelf;

        /// <summary>
        /// Shows the frame.
        /// </summary>
        public void Show()
        {
            if (m_Frame == null)
                CreateFrame();
            SetColor(m_Color);
            m_Frame.SetActive(true);
        }

        /// <summary>
        /// Hides the frame.
        /// </summary>
        public void Hide()
        {
            if (m_Frame != null)
                m_Frame.SetActive(false);
        }

        /// <summary>
        /// Sets the color of the frame lines.
        /// </summary>
        public void SetColor(Color color)
        {
            if (m_Corners == null)
                return;
            foreach (var corner in m_Corners)
            {
                corner.startColor = color;
                corner.endColor = color;
            }
        }

        /// <summary>
        /// Whether <paramref name="worldPoint"/> appears inside the frame from the camera's point of view.
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            var local = m_Camera.transform.InverseTransformPoint(worldPoint);
            if (local.z <= 0f)
                return false;
            var horizontal = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            var vertical = Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
            return Mathf.Abs(horizontal) <= m_HorizontalAngle * 0.5f && Mathf.Abs(vertical) <= m_VerticalAngle * 0.5f;
        }

        void CreateFrame()
        {
            // Parented to the camera so the frame stays in the center of view.
            m_Frame = new GameObject("Capture Frame");
            m_Frame.transform.SetParent(m_Camera.transform, false);
            m_Frame.transform.localPosition = new Vector3(0f, 0f, m_Distance);

            var halfWidth = m_Distance * Mathf.Tan(m_HorizontalAngle * 0.5f * Mathf.Deg2Rad);
            var halfHeight = m_Distance * Mathf.Tan(m_VerticalAngle * 0.5f * Mathf.Deg2Rad);
            var cornerX = halfWidth * k_CornerLength * 2f;
            var cornerY = halfHeight * k_CornerLength * 2f;

            m_Corners = new LineRenderer[4];
            var index = 0;
            foreach (var signX in new[] { -1f, 1f })
            {
                foreach (var signY in new[] { -1f, 1f })
                {
                    var corner = new Vector3(signX * halfWidth, signY * halfHeight, 0f);
                    var line = CreateLine();
                    line.SetPosition(0, corner - new Vector3(signX * cornerX, 0f, 0f));
                    line.SetPosition(1, corner);
                    line.SetPosition(2, corner - new Vector3(0f, signY * cornerY, 0f));
                    m_Corners[index++] = line;
                }
            }
        }

        LineRenderer CreateLine()
        {
            var lineObject = new GameObject("Corner");
            lineObject.transform.SetParent(m_Frame.transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = m_LineMaterial;
            line.widthMultiplier = k_LineWidth;
            line.positionCount = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
