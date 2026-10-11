using TMPro;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// Shows a short status message in front of the user for a few seconds.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class StatusToast : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The camera the message stays in front of.")]
        Camera m_Camera;

        [SerializeField]
        [Tooltip("Distance in front of the camera, in meters.")]
        float m_Distance = 1.2f;

        [SerializeField]
        [Tooltip("Offset below the center of view, in meters, so the message does not cover what the user is looking at.")]
        float m_DropBelowCenter = 0.2f;

        [SerializeField]
        [Tooltip("Minimum time each message stays visible, in seconds. Longer messages stay up long enough to read.")]
        float m_Duration = 4f;

        [SerializeField]
        [Tooltip("Reading speed used to keep longer messages up, in characters per second.")]
        float m_ReadingSpeed = 15f;

        TextMeshPro m_Text;
        float m_HideTime;

        /// <summary>
        /// Shows <paramref name="message"/>, replacing any message currently shown.
        /// </summary>
        public void Show(string message)
        {
            Debug.Log($"[Status] {message}");
            m_Text.text = message;
            m_Text.enabled = true;
            m_HideTime = Time.time + Mathf.Max(m_Duration, message.Length / m_ReadingSpeed);
        }

        void Awake()
        {
            m_Text = GetComponent<TextMeshPro>();
            m_Text.enabled = false;
        }

        void Start()
        {
            // Parented to the camera so the message stays put relative to the user's view.
            transform.SetParent(m_Camera.transform, false);
            transform.SetLocalPositionAndRotation(new Vector3(0f, -m_DropBelowCenter, m_Distance), Quaternion.identity);
        }

        void Update()
        {
            if (m_Text.enabled && Time.time >= m_HideTime)
                m_Text.enabled = false;
        }
    }
}
