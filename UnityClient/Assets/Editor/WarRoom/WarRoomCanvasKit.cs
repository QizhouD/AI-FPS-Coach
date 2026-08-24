using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FpsAiCoach.Editor
{
    /// <summary>
    /// Builds the 2D layer of the war room. Every element is anchored to its parent's centre with an
    /// explicit offset and size in canvas units, which keeps the layout arithmetic readable and
    /// removes any dependence on layout groups resolving at the right moment.
    ///
    /// One canvas serves a whole region (header, rail, deck) rather than one canvas per label, which
    /// is the single biggest cost saving over the previous scene.
    /// </summary>
    internal sealed class WarRoomCanvasKit
    {
        private readonly WarRoomTheme theme;
        private readonly TMP_FontAsset font;
        private readonly Camera worldCamera;

        public WarRoomCanvasKit(WarRoomTheme theme, TMP_FontAsset font, Camera worldCamera)
        {
            this.theme = theme;
            this.font = font;
            this.worldCamera = worldCamera;
        }

        /// <summary>Converts metres to canvas units.</summary>
        public float U(float meters) => theme.Canvas.ToUnits(meters);

        public Vector2 U(Vector2 meters) => theme.Canvas.ToUnits(meters);

        // ------------------------------------------------------------------ canvases

        public Canvas WorldCanvas(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector2 sizeMeters,
            int sortingOrder,
            Vector3 localEuler = default)
        {
            var host = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            host.transform.SetParent(parent, false);
            host.transform.localPosition = localPosition;
            host.transform.localRotation = Quaternion.Euler(localEuler);

            var rect = host.GetComponent<RectTransform>();
            rect.sizeDelta = U(sizeMeters);
            rect.localScale = Vector3.one * theme.Canvas.Scale;

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = worldCamera;
            canvas.sortingOrder = sortingOrder;

            host.GetComponent<CanvasScaler>().dynamicPixelsPerUnit =
                theme.Canvas.dynamicPixelsPerUnit;

            return canvas;
        }

        public Canvas ScreenCanvas(string name, Transform parent, int sortingOrder)
        {
            var host = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            host.transform.SetParent(parent, false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        // ------------------------------------------------------------------ primitives

        public RectTransform Rect(string name, Transform parent, Vector2 center, Vector2 size)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }

        public Image Panel(
            string name,
            Transform parent,
            Vector2 center,
            Vector2 size,
            Color color,
            bool raycastTarget = false)
        {
            var rect = Rect(name, parent, center, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>
        /// Left-pivoted fill used by metric bars, so growing the value only writes sizeDelta.x.
        /// </summary>
        public Image LeftFill(
            string name,
            Transform parent,
            Vector2 leftEdgeCenter,
            Vector2 size,
            Color color)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = leftEdgeCenter;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            var image = host.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// World-space text. <paramref name="emMeters"/> is the em height in metres; pass
        /// <paramref name="fontSizeOverride"/> instead when authoring on a screen-space canvas, where
        /// sizes are reference pixels rather than metres.
        /// </summary>
        public TMP_Text Label(
            string name,
            Transform parent,
            Vector2 center,
            Vector2 size,
            string content,
            float emMeters,
            Color color,
            TextAlignmentOptions alignment,
            float tracking,
            FontStyles style = FontStyles.Normal,
            bool wrap = false,
            float? fontSizeOverride = null,
            float? autoSizeFloor = null)
        {
            var rect = Rect(name, parent, center, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSizeOverride ?? U(emMeters);
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = tracking;
            text.fontStyle = style;
            text.lineSpacing = -6f;
            text.textWrappingMode = wrap
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.outlineColor = theme.Text.outlineColor;
            text.outlineWidth = theme.Text.outlineWidth;

            if (autoSizeFloor.HasValue)
            {
                // For labels filled from the analysis service, whose length this project does not
                // control: TMP shrinks toward the floor to fit the box, and truncates rather than
                // spilling past the panel if even the floor is too large.
                text.enableAutoSizing = true;
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = U(autoSizeFloor.Value);
                text.overflowMode = TextOverflowModes.Truncate;
            }
            else
            {
                text.overflowMode = TextOverflowModes.Overflow;
            }

            return text;
        }

        /// <summary>A one-unit-thick divider, used instead of decorative frames.</summary>
        public Image Divider(
            string name,
            Transform parent,
            Vector2 center,
            float width,
            float thickness,
            Color color)
        {
            return Panel(name, parent, center, new Vector2(width, thickness), color);
        }

        // ------------------------------------------------------------------ interactive

        /// <summary>
        /// Border-plus-fill button. The outer Image is the border, an inset child is the fill, and a
        /// BoxCollider makes it reachable by the world-space ray interactor.
        ///
        /// <paramref name="fillColor"/> must be effectively opaque: the border Image spans the whole
        /// rect, so a translucent fill lets the border colour bleed across the entire button instead of
        /// leaving a rim.
        /// </summary>
        public StudioHudController.DeckButton GhostButton(
            string name,
            Transform parent,
            Vector2 center,
            Vector2 size,
            string label,
            Color borderColor,
            Color fillColor,
            Color textColor,
            float borderUnits,
            float colliderDepthUnits,
            float? labelEmMeters = null)
        {
            var rect = Rect(name, parent, center, size);
            var host = rect.gameObject;

            var border = host.AddComponent<Image>();
            border.color = borderColor;
            border.raycastTarget = false;

            var fill = Panel(
                "Fill",
                host.transform,
                Vector2.zero,
                new Vector2(size.x - borderUnits * 2f, size.y - borderUnits * 2f),
                Color.white);

            var text = Label(
                "Label",
                host.transform,
                Vector2.zero,
                new Vector2(size.x - borderUnits * 6f, size.y),
                label,
                labelEmMeters ?? theme.Text.buttonLabel,
                textColor,
                TextAlignmentOptions.Center,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            var button = host.AddComponent<Button>();
            button.targetGraphic = fill;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = fillColor,
                highlightedColor = Color.Lerp(fillColor, Color.white, 0.22f),
                pressedColor = Color.Lerp(fillColor, Color.black, 0.2f),
                selectedColor = fillColor,
                // Darkened rather than faded, so the border never shows through a disabled fill.
                disabledColor = WarRoomColor.WithAlpha(
                    Color.Lerp(fillColor, Color.black, 0.5f),
                    fillColor.a),
                colorMultiplier = 1f,
                fadeDuration = 0.07f
            };

            var collider = host.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, colliderDepthUnits);

            return new StudioHudController.DeckButton
            {
                button = button,
                fill = fill,
                border = border,
                label = text
            };
        }

        /// <summary>
        /// Clickable list row. Colours are owned by <see cref="MatchLibraryController"/>, so the
        /// Button tints a dedicated transparent hover layer instead of fighting the controller.
        /// </summary>
        public MatchLibraryController.Row ListRow(
            string name,
            Transform parent,
            Vector2 center,
            Vector2 size,
            WarRoomTheme.MatchEntry entry,
            float borderUnits,
            float indicatorWidth,
            float colliderDepthUnits)
        {
            var rect = Rect(name, parent, center, size);
            var host = rect.gameObject;

            var border = host.AddComponent<Image>();
            border.raycastTarget = false;

            var background = Panel(
                "Background",
                host.transform,
                Vector2.zero,
                new Vector2(size.x - borderUnits * 2f, size.y - borderUnits * 2f),
                Color.clear);

            var hover = Panel(
                "Hover",
                host.transform,
                Vector2.zero,
                new Vector2(size.x - borderUnits * 2f, size.y - borderUnits * 2f),
                Color.white);

            var indicator = Panel(
                "Indicator",
                host.transform,
                new Vector2(-size.x * 0.5f + indicatorWidth * 0.5f + borderUnits, 0f),
                new Vector2(indicatorWidth, size.y - borderUnits * 6f),
                Color.white);

            var textLeft = -size.x * 0.5f + indicatorWidth + U(0.14f);
            var mapWidth = size.x * 0.55f;
            var map = Label(
                "Map",
                host.transform,
                new Vector2(textLeft + mapWidth * 0.5f, size.y * 0.17f),
                new Vector2(mapWidth, size.y * 0.45f),
                entry.map,
                theme.Text.rowPrimary,
                Color.white,
                TextAlignmentOptions.Left,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            var score = Label(
                "Score",
                host.transform,
                new Vector2(size.x * 0.5f - U(0.14f) - mapWidth * 0.5f, size.y * 0.17f),
                new Vector2(mapWidth, size.y * 0.45f),
                entry.score,
                theme.Text.rowPrimary,
                Color.white,
                TextAlignmentOptions.Right,
                theme.Text.trackingBody,
                FontStyles.Bold);

            // An analyzed report overwrites this with a player name of unknown length, so it shrinks
            // rather than running under the score column.
            var meta = Label(
                "Meta",
                host.transform,
                new Vector2(textLeft + size.x * 0.35f, -size.y * 0.24f),
                new Vector2(size.x * 0.7f, size.y * 0.35f),
                entry.meta,
                theme.Text.rowSecondary,
                WarRoomColor.ForUi(theme.Colors.textMuted),
                TextAlignmentOptions.Left,
                theme.Text.trackingBody,
                FontStyles.Normal,
                wrap: false,
                autoSizeFloor: theme.Text.cardBodyFloor);

            var button = host.AddComponent<Button>();
            button.targetGraphic = hover;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = Color.clear,
                highlightedColor = new Color(1f, 1f, 1f, 0.06f),
                pressedColor = new Color(1f, 1f, 1f, 0.11f),
                selectedColor = Color.clear,
                disabledColor = Color.clear,
                colorMultiplier = 1f,
                fadeDuration = 0.07f
            };

            var collider = host.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, colliderDepthUnits);

            return new MatchLibraryController.Row
            {
                button = button,
                background = background,
                border = border,
                indicator = indicator,
                mapLabel = map,
                scoreLabel = score,
                metaLabel = meta
            };
        }
    }
}
