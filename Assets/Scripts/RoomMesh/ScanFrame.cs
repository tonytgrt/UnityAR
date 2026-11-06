using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAR
{
    /// <summary>
    /// A viewfinder frame fixed in the center of the user's view, used to aim at a scan image.
    /// </summary>
    /// <remarks>
    /// Give it a material that draws on top of everything, so the frame stays visible when a real-world surface,
    /// such as a desk, is closer than the frame.
    /// </remarks>
    public class ScanFrame : MonoBehaviour
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
        float m_VerticalAngle = 14f;

        [SerializeField]
        [Tooltip("Distance of the frame in front of the camera, in meters.")]
        float m_Distance = 1f;

        [SerializeField]
        Color m_Color = Color.white;

        GameObject m_Frame;

        /// <summary>
        /// Shows the frame.
        /// </summary>
        public void Show()
        {
            if (m_Frame == null)
                CreateFrame();
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
            m_Frame = new GameObject("Scan Frame");
            m_Frame.transform.SetParent(m_Camera.transform, false);
            m_Frame.transform.localPosition = new Vector3(0f, 0f, m_Distance);

            var halfWidth = m_Distance * Mathf.Tan(m_HorizontalAngle * 0.5f * Mathf.Deg2Rad);
            var halfHeight = m_Distance * Mathf.Tan(m_VerticalAngle * 0.5f * Mathf.Deg2Rad);
            var cornerX = halfWidth * k_CornerLength * 2f;
            var cornerY = halfHeight * k_CornerLength * 2f;

            foreach (var signX in new[] { -1f, 1f })
            {
                foreach (var signY in new[] { -1f, 1f })
                {
                    var corner = new Vector3(signX * halfWidth, signY * halfHeight, 0f);
                    var line = CreateLine();
                    line.SetPosition(0, corner - new Vector3(signX * cornerX, 0f, 0f));
                    line.SetPosition(1, corner);
                    line.SetPosition(2, corner - new Vector3(0f, signY * cornerY, 0f));
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
            line.startColor = m_Color;
            line.endColor = m_Color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
