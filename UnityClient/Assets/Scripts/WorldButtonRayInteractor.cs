using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FpsAiCoach
{
    public sealed class WorldButtonRayInteractor : MonoBehaviour
    {
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private float maxDistance = 100f;
        [SerializeField] private Color idleColor = new Color(0.2f, 0.95f, 1f, 0.9f);
        [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 1f);

        private RectTransform crosshair;
        private Image[] crosshairGraphics;
        private Button hoveredButton;
        private PointerEventData pointerData;
        private Vector2 pointerPosition;
        private int lastClickFrame = -1;

        private void Awake()
        {
            if (interactionCamera == null)
                interactionCamera = Camera.main;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            pointerData = new PointerEventData(EventSystem.current);
            CreateCrosshair();
            pointerPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        private void OnDisable()
        {
            SetHoveredButton(null);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                return;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Update()
        {
            var legacyPosition = (Vector2)Input.mousePosition;
            if (IsInsideScreen(legacyPosition))
                pointerPosition = legacyPosition;

            ProcessPointer(
                pointerPosition,
                Input.GetMouseButtonDown(0),
                Input.GetMouseButtonUp(0));
        }

        private void OnGUI()
        {
            var currentEvent = Event.current;
            if (
                currentEvent == null ||
                (
                    currentEvent.type != EventType.MouseMove &&
                    currentEvent.type != EventType.MouseDrag &&
                    currentEvent.type != EventType.MouseDown &&
                    currentEvent.type != EventType.MouseUp))
            {
                return;
            }

            pointerPosition = new Vector2(
                currentEvent.mousePosition.x,
                Screen.height - currentEvent.mousePosition.y);
            ProcessPointer(
                pointerPosition,
                currentEvent.type == EventType.MouseDown && currentEvent.button == 0,
                currentEvent.type == EventType.MouseUp && currentEvent.button == 0);
        }

        private void ProcessPointer(
            Vector2 screenPosition,
            bool pressedThisFrame,
            bool releasedThisFrame)
        {
            if (interactionCamera == null || crosshair == null)
                return;

            crosshair.position = screenPosition;
            var ray = interactionCamera.ScreenPointToRay(screenPosition);
            Button targetButton = null;
            if (Physics.Raycast(ray, out var hit, maxDistance))
                targetButton = hit.collider.GetComponent<Button>();

            if (targetButton != null && !targetButton.IsInteractable())
                targetButton = null;

            SetHoveredButton(targetButton);
            SetCrosshairColor(targetButton != null ? hoverColor : idleColor);

            if (
                hoveredButton != null &&
                pressedThisFrame &&
                lastClickFrame != Time.frameCount)
            {
                lastClickFrame = Time.frameCount;
                hoveredButton.OnPointerDown(pointerData);
                hoveredButton.onClick.Invoke();
                Debug.Log("World button ray clicked: " + hoveredButton.name);
            }

            if (hoveredButton != null && releasedThisFrame)
                hoveredButton.OnPointerUp(pointerData);
        }

        private void SetHoveredButton(Button button)
        {
            if (hoveredButton == button)
                return;

            if (hoveredButton != null)
                hoveredButton.OnPointerExit(pointerData);

            hoveredButton = button;
            if (hoveredButton != null)
                hoveredButton.OnPointerEnter(pointerData);
        }

        private void CreateCrosshair()
        {
            var canvasObject = new GameObject(
                "FPS Crosshair Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;

            var crosshairObject = new GameObject(
                "FPS Crosshair",
                typeof(RectTransform));
            crosshairObject.transform.SetParent(canvasObject.transform, false);
            crosshair = crosshairObject.GetComponent<RectTransform>();
            crosshair.anchorMin = Vector2.zero;
            crosshair.anchorMax = Vector2.zero;
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            crosshair.sizeDelta = new Vector2(34f, 34f);

            crosshairGraphics = new[]
            {
                CreateCrosshairBar("Horizontal", new Vector2(30f, 2f)),
                CreateCrosshairBar("Vertical", new Vector2(2f, 30f)),
                CreateCrosshairBar("Center", new Vector2(4f, 4f))
            };
            SetCrosshairColor(idleColor);
        }

        private Image CreateCrosshairBar(string objectName, Vector2 size)
        {
            var barObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            barObject.transform.SetParent(crosshair, false);
            var rect = barObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var image = barObject.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private void SetCrosshairColor(Color color)
        {
            foreach (var graphic in crosshairGraphics)
                graphic.color = color;
        }

        private static bool IsInsideScreen(Vector2 position)
        {
            return
                position.x >= 0f &&
                position.y >= 0f &&
                position.x <= Screen.width &&
                position.y <= Screen.height;
        }
    }
}
