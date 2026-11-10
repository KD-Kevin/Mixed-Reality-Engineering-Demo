using System;
using UnityEngine;

namespace EngineeringDemo
{
    internal sealed class FridgePartCallout
    {
        private readonly Canvas _canvas;
        private readonly RectTransform _root;
        private readonly RectTransform _line;
        private readonly RectTransform _card;
        private readonly RectTransform _back;
        private readonly RectTransform _reset;
        private readonly RectTransform _close;
        private readonly UnityEngine.UI.Text _label;
        private readonly UnityEngine.UI.Image _detectionLabel;

        private Camera CanvasCamera => _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        internal FridgePartCallout(UnityEngine.UI.Text style, Action collapse, Action resetRotation, Action closeHologram)
        {
            _canvas = style.canvas.rootCanvas;
            var canvasRoot = _canvas.transform;
            int existingControls = canvasRoot.childCount;
            _root = new GameObject("Fridge Safe Area", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(canvasRoot, false);
            Stretch(_root, Vector2.zero);
            for (int i = 0; i < existingControls; i++)
                canvasRoot.GetChild(0).SetParent(_root, false);

            var detectionTag = CreateImage("Fridge Detection Label", _root, Color.cyan);
            detectionTag.sizeDelta = new Vector2(82f, 26f);
            detectionTag.SetAsFirstSibling();
            _detectionLabel = detectionTag.GetComponent<UnityEngine.UI.Image>();
            var detectionText = new GameObject("Fridge Text", typeof(RectTransform), typeof(UnityEngine.UI.Text))
                .GetComponent<UnityEngine.UI.Text>();
            detectionText.transform.SetParent(detectionTag, false);
            Stretch(detectionText.rectTransform, Vector2.zero);
            detectionText.font = style.font;
            detectionText.fontSize = 16;
            detectionText.fontStyle = FontStyle.Bold;
            detectionText.color = new Color(0.04f, 0.09f, 0.14f);
            detectionText.alignment = TextAnchor.MiddleCenter;
            detectionText.raycastTarget = false;
            detectionText.text = "Fridge";
            detectionTag.gameObject.SetActive(false);

            var accent = FridgeSection.HoverColor;
            accent.a = 1f;
            _line = CreateImage("Leader Line", _root, accent);
            _line.pivot = new Vector2(0f, 0.5f);
            _card = CreateImage("Part Label", _root, new Color(0.04f, 0.09f, 0.14f, 0.94f));
            _card.sizeDelta = new Vector2(236f, 60f);
            _label = new GameObject("Label Text", typeof(RectTransform), typeof(UnityEngine.UI.Text))
                .GetComponent<UnityEngine.UI.Text>();
            _label.transform.SetParent(_card, false);
            Stretch(_label.rectTransform, new Vector2(10f, 3f));
            _label.font = style.font;
            _label.fontSize = 17;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.raycastTarget = false;
            _back = CreateButton("Back to Fridge", style.font, collapse);
            _reset = CreateButton("Reset Rotation", style.font, resetRotation);
            _close = CreateButton("Close Hologram", style.font, closeHologram);
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID
            var closeDemo = CreateButton("Close Demo", style.font, () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            closeDemo.anchorMin = closeDemo.anchorMax = closeDemo.pivot = Vector2.right;
            closeDemo.anchoredPosition = new Vector2(-16f, 72f);
#endif
            Show(null, default, false, false, false);
        }

        internal void Show(Camera camera, FridgeHologram.SectionHit hit, bool expanded, bool canRotate, bool hologramVisible)
        {
            ApplySafeArea(Screen.safeArea, Screen.width, Screen.height);
            _close.gameObject.SetActive(hologramVisible);
            _back.gameObject.SetActive(expanded);
            _back.anchoredPosition = new Vector2(16f, 72f);
            _reset.gameObject.SetActive(canRotate);
            _reset.anchoredPosition = new Vector2(16f, expanded ? 128f : 72f);
            bool visible = hit.Section != null;
            _line.gameObject.SetActive(visible);
            _card.gameObject.SetActive(visible);
            if (!visible)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, camera.WorldToScreenPoint(hit.Point),
                CanvasCamera, out var start);

            var part = hit.Part;
            bool hasReference = !string.IsNullOrEmpty(part.ReferenceId);
            var canvasRect = _root.rect;
            var size = new Vector2(Mathf.Min(248f, canvasRect.width * 0.48f), 60f);
            _card.sizeDelta = size;
            var half = size * 0.5f;
            float side = start.x < canvasRect.center.x ? 1f : -1f;
            var position = start + new Vector2(side * (half.x + 54f), 34f);
            position.x = Mathf.Clamp(position.x, canvasRect.xMin + half.x + 8f, canvasRect.xMax - half.x - 8f);
            position.y = Mathf.Clamp(position.y, canvasRect.yMin + half.y + 8f, canvasRect.yMax - half.y - 8f);
            _card.anchoredPosition = position;
            string name = expanded ? part.Name : hit.Section.name;
            _label.text = hasReference ? name + "\nLTCS20020S ref. " + part.ReferenceId : name;

            var end = position - new Vector2(side * half.x, 0f);
            var direction = end - start;
            _line.anchoredPosition = start;
            _line.sizeDelta = new Vector2(direction.magnitude, 2f);
            _line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        internal void ShowDetection(Vector2? topEdge, Color color)
        {
            Vector2 edge = default;
            bool visible = topEdge is Vector2 point && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _root, point, CanvasCamera, out edge);
            _detectionLabel.gameObject.SetActive(visible);
            if (!visible)
                return;

            var tag = _detectionLabel.rectTransform;
            var half = tag.sizeDelta * 0.5f;
            var bounds = _root.rect;
            const float gap = 2f;
            float y = edge.y + half.y + gap;
            if (y + half.y > bounds.yMax)
                y = edge.y - half.y - gap;

            tag.anchoredPosition = new Vector2(
                Mathf.Clamp(edge.x, bounds.xMin + half.x, bounds.xMax - half.x),
                Mathf.Clamp(y, bounds.yMin + half.y, bounds.yMax - half.y));
            _detectionLabel.color = color;
        }

        private void ApplySafeArea(Rect area, int width, int height)
        {
            if (width <= 0 || height <= 0 || area.width <= 0f || area.height <= 0f)
                return;

            _root.anchorMin = new Vector2(area.xMin / width, area.yMin / height);
            _root.anchorMax = new Vector2(area.xMax / width, area.yMax / height);
        }

        private RectTransform CreateButton(string title, Font font, Action onClick)
        {
            var rect = CreateImage(title, _root, new Color(0.1f, 0.21f, 0.29f, 0.96f));
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(148f, 48f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            button.targetGraphic.raycastTarget = true;
            button.onClick.AddListener(() => onClick());

            var label = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text))
                .GetComponent<UnityEngine.UI.Text>();
            label.transform.SetParent(rect, false);
            Stretch(label.rectTransform, Vector2.zero);
            label.font = font;
            label.fontSize = 17;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = title;
            return rect;
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.color = color;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private static void Stretch(RectTransform rect, Vector2 padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = padding;
            rect.offsetMax = -padding;
        }
    }
}
