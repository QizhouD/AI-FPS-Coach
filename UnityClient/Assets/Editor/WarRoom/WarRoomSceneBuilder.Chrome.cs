using TMPro;
using UnityEditor;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    public static partial class WarRoomSceneBuilder
    {
        /// <summary>
        /// Header band: one canvas carrying the brand line, the selected match and a mode chip, plus
        /// a small emissive beacon. The beacon is geometry rather than a light, which keeps the scene
        /// inside its three-light budget.
        /// </summary>
        private static void BuildHeader(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var header = theme.Header;
            var palette = theme.Colors;
            var ui = context.Ui;

            var group = WarRoomGeometry.Group(
                "Header",
                context.Root.transform,
                new Vector3(0f, header.centerY, header.centerZ));

            var beacon = WarRoomGeometry.Sphere(
                "Status Beacon",
                group.transform,
                header.beaconPosition - new Vector3(0f, header.centerY, header.centerZ),
                header.beaconDiameter,
                context.Materials.LineCyan);
            context.Beacon = beacon.transform;

            var canvas = ui.WorldCanvas(
                "Canvas",
                group.transform,
                Vector3.zero,
                header.canvasSize,
                theme.Canvas.sortingOrderHeader);

            var half = ui.U(header.canvasSize.x) * 0.5f;
            var height = ui.U(header.canvasSize.y);
            var padding = ui.U(0.12f);

            // Rich text keeps the brand lockup in a single label, so the divider and the module name
            // can never drift out of alignment with the product name.
            var mutedHex = ColorUtility.ToHtmlStringRGB(WarRoomColor.ForUi(palette.textMuted));
            var secondaryHex = ColorUtility.ToHtmlStringRGB(WarRoomColor.ForUi(palette.textSecondary));
            var brand =
                $"{theme.Data.productName}" +
                $"<size=52%><color=#{mutedHex}>   /   </color>" +
                $"<color=#{secondaryHex}>{theme.Data.moduleName}</color></size>";

            var brandWidth = ui.U(6f);
            var brandLeft = -half + ui.U(0.65f);
            ui.Label(
                "Brand",
                canvas.transform,
                new Vector2(brandLeft + brandWidth * 0.5f, 0f),
                new Vector2(brandWidth, height),
                brand,
                theme.Text.headerTitle,
                WarRoomColor.ForUi(palette.textPrimary),
                TextAlignmentOptions.Left,
                theme.Text.trackingTitle,
                FontStyles.Bold);

            // Mode chip
            var chipSize = new Vector2(ui.U(1.05f), ui.U(0.38f));
            var chipCenter = new Vector2(half - padding - chipSize.x * 0.5f, 0f);
            var chipBorder = ui.U(0.012f);

            var chip = ui.Panel(
                "Mode Chip",
                canvas.transform,
                chipCenter,
                chipSize,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanDim), 0.85f));

            ui.Panel(
                "Fill",
                chip.transform,
                Vector2.zero,
                new Vector2(chipSize.x - chipBorder * 2f, chipSize.y - chipBorder * 2f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelGlass), 0.92f));

            context.HeaderModeLabel = ui.Label(
                "Mode",
                chip.transform,
                Vector2.zero,
                new Vector2(chipSize.x, chipSize.y),
                theme.Data.modeDemo,
                theme.Text.headerMeta,
                WarRoomColor.ForUi(palette.cyanPrimary),
                TextAlignmentOptions.Center,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            var matchWidth = ui.U(3f);
            var matchRight = chipCenter.x - chipSize.x * 0.5f - padding;
            context.HeaderMatchLabel = ui.Label(
                "Selected Match",
                canvas.transform,
                new Vector2(matchRight - matchWidth * 0.5f, 0f),
                new Vector2(matchWidth, height),
                string.Empty,
                theme.Text.headerMeta,
                WarRoomColor.ForUi(palette.textBright),
                TextAlignmentOptions.Right,
                theme.Text.trackingLabel,
                FontStyles.Bold);
        }

        /// <summary>
        /// Control deck: a tilted console slab with all three buttons on a single canvas. Play is the
        /// only filled button; the others are ghosts, with amber reserved for the live action.
        /// </summary>
        private static void BuildControlDeck(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var deck = theme.Deck;
            var palette = theme.Colors;
            var materials = context.Materials;
            var ui = context.Ui;

            var group = WarRoomGeometry.Group("Control Deck", context.Root.transform);

            var slab = WarRoomGeometry.Box(
                "Deck",
                group.transform,
                deck.deckCenter,
                deck.deckSize,
                materials.PanelGlass,
                castShadows: true,
                receiveShadows: true);
            slab.transform.localRotation = Quaternion.Euler(deck.deckTiltX, 0f, 0f);

            WarRoomGeometry.BarX(
                "Deck Edge",
                group.transform,
                new Vector3(
                    0f,
                    deck.deckCenter.y + deck.deckSize.y * 0.5f,
                    deck.deckCenter.z - deck.deckSize.z * 0.5f + 0.06f),
                deck.deckSize.x - 0.4f,
                deck.deckEdgeThickness,
                materials.LineCyanDim);

            var canvas = ui.WorldCanvas(
                "Canvas",
                group.transform,
                new Vector3(0f, deck.buttonRowY, deck.buttonRowZ),
                deck.canvasSize,
                theme.Canvas.sortingOrderDeck);

            var size = ui.U(deck.buttonSize);
            var spacing = ui.U(deck.buttonSpacing);
            var border = ui.U(deck.buttonBorder);
            var colliderDepth = ui.U(deck.buttonColliderDepth);
            var ghostFill = WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelGlass), 0.92f);

            context.ImportButton = ui.GhostButton(
                "IMPORT VIDEO Button",
                canvas.transform,
                new Vector2(-spacing, 0f),
                size,
                theme.Data.buttonImport,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanDim), 0.9f),
                ghostFill,
                WarRoomColor.ForUi(palette.textPrimary),
                border,
                colliderDepth);

            // Primary action. An opaque dark-teal body with a full-strength cyan rim and label carries
            // the hierarchy; a solid cyan block would out-shout the tactical screen behind it.
            var primaryFill = Color.Lerp(
                WarRoomColor.ForUi(palette.voidBackdrop),
                WarRoomColor.ForUi(palette.cyanDim),
                0.25f);

            context.PlayButton = ui.GhostButton(
                "PLAY Button",
                canvas.transform,
                Vector2.zero,
                size,
                theme.Data.buttonPlay,
                WarRoomColor.ForUi(palette.cyanPrimary),
                primaryFill,
                WarRoomColor.ForUi(palette.cyanPrimary),
                border,
                colliderDepth);

            // Amber survives only on the label. Giving this button an orange border would put the
            // priority colour in two unrelated places and dilute what it signals on the insight cards.
            context.LiveButton = ui.GhostButton(
                "LIVE MODE Button",
                canvas.transform,
                new Vector2(spacing, 0f),
                size,
                theme.Data.buttonLive,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanDim), 0.9f),
                ghostFill,
                WarRoomColor.ForUi(palette.amberAlert),
                border,
                colliderDepth);

            context.PlayButton.button.interactable = false;
        }

        /// <summary>
        /// The 2D half of the hybrid presentation: a screen-space frame that stays pixel-crisp at any
        /// resolution and never competes with the 3D layer for depth.
        /// </summary>
        private static void BuildScreenSpaceFrame(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var palette = theme.Colors;
            var ui = context.Ui;

            var canvas = ui.ScreenCanvas(
                "Screen Space Frame",
                context.Root.transform,
                theme.Canvas.sortingOrderScreenSpace);

            var bracketColor = WarRoomColor.WithAlpha(
                WarRoomColor.ForUi(palette.cyanDim),
                0.5f);

            const float halfX = 960f;
            const float halfY = 540f;
            const float margin = 26f;
            const float length = 96f;
            const float thickness = 2f;

            for (var corner = 0; corner < 4; corner++)
            {
                var signX = (corner & 1) == 0 ? -1f : 1f;
                var signY = (corner & 2) == 0 ? -1f : 1f;
                var label = $"{(signY < 0f ? "Bottom" : "Top")} {(signX < 0f ? "Left" : "Right")}";

                ui.Panel(
                    $"Bracket {label} H",
                    canvas.transform,
                    new Vector2(
                        signX * (halfX - margin - length * 0.5f),
                        signY * (halfY - margin)),
                    new Vector2(length, thickness),
                    bracketColor);

                ui.Panel(
                    $"Bracket {label} V",
                    canvas.transform,
                    new Vector2(
                        signX * (halfX - margin),
                        signY * (halfY - margin - length * 0.5f)),
                    new Vector2(thickness, length),
                    bracketColor);
            }

            ui.Label(
                "Build Tag",
                canvas.transform,
                new Vector2(-halfX + margin + 400f, -halfY + 54f),
                new Vector2(800f, 44f),
                $"{theme.Data.productName}  //  {theme.Data.buildTag}",
                0f,
                WarRoomColor.ForUi(palette.textMuted),
                TextAlignmentOptions.Left,
                theme.Text.trackingLabel,
                FontStyles.Normal,
                wrap: false,
                fontSizeOverride: 18f);

            ui.Label(
                "Interaction Hint",
                canvas.transform,
                new Vector2(halfX - margin - 500f, -halfY + 54f),
                new Vector2(1000f, 44f),
                "CLICK TO SELECT  ·  IMPORT A CLIP TO BEGIN REVIEW",
                0f,
                WarRoomColor.ForUi(palette.textMuted),
                TextAlignmentOptions.Right,
                theme.Text.trackingLabel,
                FontStyles.Normal,
                wrap: false,
                fontSizeOverride: 18f);
        }
    }
}
