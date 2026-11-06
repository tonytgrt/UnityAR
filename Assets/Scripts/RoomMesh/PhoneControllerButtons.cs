using System;
using TMPro;
using Unity.XR.XREAL;
using UnityEngine;
using UnityEngine.UI;

namespace UnityAR
{
    /// <summary>
    /// Adds buttons for saving and loading the room and for managing cubes to XREAL's phone controller screen.
    /// </summary>
    /// <remarks>
    /// The buttons are created at runtime inside the phone controller's canvas, in a 2 x 2 grid between the XREAL
    /// logo and the APP button. A button is highlighted while its action is armed or active.
    /// </remarks>
    public class PhoneControllerButtons : MonoBehaviour
    {
        class PhoneButton
        {
            public Image background;
            public Func<bool> isActive;
        }

        // Canvas units. XREAL's controller canvas scales to a 1080 x 2340 reference, matching height.
        static readonly Vector2 k_ButtonSize = new Vector2(470f, 150f);
        const float k_ColumnOffset = 250f;
        const float k_FirstRowFromTop = 270f;
        const float k_RowSpacing = 170f;
        const float k_FontSize = 44f;

        static readonly Color k_IdleColor = new Color(0.1f, 0.1f, 0.1f);
        static readonly Color k_ActiveColor = new Color(0.1f, 0.45f, 0.38f);
        static readonly Color k_BorderColor = new Color(0.3f, 0.85f, 0.7f);

        [SerializeField]
        ImageTargetController m_ImageTargets;

        [SerializeField]
        RoomMeshStore m_Store;

        [SerializeField]
        CubePlacer m_Cubes;

        [SerializeField]
        StatusToast m_Status;

        PhoneButton[] m_Buttons;

        void Update()
        {
            // XREAL creates the phone controller when its XR loader starts.
            if (m_Buttons == null)
            {
                var controller = XREALVirtualController.Singleton;
                var canvas = controller != null ? controller.GetComponentInChildren<Canvas>(true) : null;
                if (canvas == null)
                    return;
                CreateButtons(canvas);
            }

            foreach (var button in m_Buttons)
                button.background.color = button.isActive() ? k_ActiveColor : k_IdleColor;
        }

        void CreateButtons(Canvas canvas)
        {
            var panel = new GameObject("Project Buttons", typeof(RectTransform));
            panel.layer = canvas.gameObject.layer;
            var panelTransform = (RectTransform)panel.transform;
            panelTransform.SetParent(canvas.transform, false);
            panelTransform.anchorMin = Vector2.zero;
            panelTransform.anchorMax = Vector2.one;
            panelTransform.offsetMin = Vector2.zero;
            panelTransform.offsetMax = Vector2.zero;

            m_Buttons = new[]
            {
                CreateButton(panelTransform, "Save to image", 0, 0, m_ImageTargets.ToggleSave,
                    () => m_ImageTargets.armedAction == ImageTargetController.ImageAction.Save),
                CreateButton(panelTransform, "Load from image", 1, 0, m_ImageTargets.ToggleLoad,
                    () => m_ImageTargets.armedAction == ImageTargetController.ImageAction.Load),
                CreateButton(panelTransform, "Rescan", 0, 1, Rescan, () => !m_Store.isLiveScanning),
                CreateButton(panelTransform, "Clear cubes", 1, 1, ClearCubes, () => false),
            };
        }

        void Rescan()
        {
            if (m_Store.isLiveScanning)
            {
                m_Status.Show("Already scanning the room.");
                return;
            }

            m_Store.ResumeLiveScanning();
            m_Status.Show("Rescanning the room. The cubes stay where they are.");
        }

        void ClearCubes()
        {
            var count = m_Cubes.cubes.Count;
            m_Cubes.Clear();
            m_Status.Show(count == 0 ? "There are no cubes to clear." : "Cleared the cubes. Rooms saved to a scan image keep theirs.");
        }

        static PhoneButton CreateButton(RectTransform parent, string label, int column, int row,
            UnityEngine.Events.UnityAction onClick, Func<bool> isActive)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            buttonObject.layer = parent.gameObject.layer;
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.sizeDelta = k_ButtonSize;
            rect.anchoredPosition = new Vector2(
                column == 0 ? -k_ColumnOffset : k_ColumnOffset,
                -(k_FirstRowFromTop + row * k_RowSpacing));

            var background = buttonObject.GetComponent<Image>();
            background.color = k_IdleColor;
            var outline = buttonObject.GetComponent<Outline>();
            outline.effectColor = k_BorderColor;
            outline.effectDistance = new Vector2(3f, -3f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.layer = parent.gameObject.layer;
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = k_FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            return new PhoneButton { background = background, isActive = isActive };
        }
    }
}
