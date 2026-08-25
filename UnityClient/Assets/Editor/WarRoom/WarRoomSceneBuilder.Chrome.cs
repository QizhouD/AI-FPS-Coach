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
        /// Control deck: a tilted console slab carrying every button on a single canvas, in two groups
        /// separated by a wider gap. The left group chooses what the tactical screen shows; the right
        /// group captures it. Play is the only filled button; the others are ghosts, with amber
        /// reserved for the live action.
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
            var gap = ui.U(deck.buttonGap);
            var groupGap = ui.U(deck.buttonGroupGap);
            var border = ui.U(deck.buttonBorder);
            var colliderDepth = ui.U(deck.buttonColliderDepth);
            var ghostFill = WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelGlass), 0.92f);

            // Slot centres are derived rather than authored, so the row stays centred and evenly
            // spaced no matter how the widths and gaps are retuned. Index 3 opens the capture group.
            const int slots = 5;
            const int captureGroupStart = 3;
            var span = size.x * slots + gap * (slots - 2) + groupGap;
            var slotX = new float[slots];
            var cursor = -span * 0.5f;

            for (var slot = 0; slot < slots; slot++)
            {
                if (slot > 0)
                    cursor += slot == captureGroupStart ? groupGap : gap;

                slotX[slot] = cursor + size.x * 0.5f;
                cursor += size.x;
            }

            context.ImportButton = ui.GhostButton(
                "IMPORT VIDEO Button",
                canvas.transform,
                new Vector2(slotX[0], 0f),
                size,
                theme.Data.buttonImport,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanDim), 0.9f),
                ghostFill,
                WarRoomColor.ForUi(palette.textPrimary),
                border,
                colliderDepth,
                autoSizeFloor: theme.Text.footerButtonLabel);

            // Primary action. An opaque dark-teal body with a full-strength cyan rim and label carries
            // the hierarchy; a solid cyan block would out-shout the tactical screen behind it.
            var primaryFill = Color.Lerp(
                WarRoomColor.ForUi(palette.voidBackdrop),
                WarRoomColor.ForUi(palette.cyanDim),
                0.25f);

            context.PlayButton = ui.GhostButton(
                "PLAY Button",
                canvas.transform,
                new Vector2(slotX[1], 0f),
                size,
                theme.Data.buttonPlay,
                WarRoomColor.ForUi(palette.cyanPrimary),
                primaryFill,
                WarRoomColor.ForUi(palette.cyanPrimary),
                border,
                colliderDepth,
                autoSizeFloor: theme.Text.footerButtonLabel);

            // Amber survives only on the label. Giving this button an orange border would put the
            // priority colour in two unrelated places and dilute what it signals on the insight cards.
            context.LiveButton = ui.GhostButton(
                "LIVE MODE Button",
                canvas.transform,
                new Vector2(slotX[2], 0f),
                size,
                theme.Data.buttonLive,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanDim), 0.9f),
                ghostFill,
                WarRoomColor.ForUi(palette.amberAlert),
                border,
                colliderDepth,
                autoSizeFloor: theme.Text.footerButtonLabel);

            // Capture group. Both stay muted ghosts: recording is a supporting action, and the deck
            // already spends its one strong accent on PLAY. The recording state is carried by the
            // status line and the label swap, not by a colour that competes with the screen.
            context.RecordButton = ui.GhostButton(
                "RECORD Button",
                canvas.transform,
                new Vector2(slotX[3], 0f),
                size,
                theme.Data.buttonRecord,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.95f),
                ghostFill,
                WarRoomColor.ForUi(palette.textBright),
                border,
                colliderDepth,
                autoSizeFloor: theme.Text.footerButtonLabel);

            context.SaveClipButton = ui.GhostButton(
                "SAVE CLIP Button",
                canvas.transform,
                new Vector2(slotX[4], 0f),
                size,
                theme.Data.buttonSaveClip,
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.95f),
                ghostFill,
                WarRoomColor.ForUi(palette.textSecondary),
                border,
                colliderDepth,
                autoSizeFloor: theme.Text.footerButtonLabel);

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

            // Capture state lives here rather than on the deck: the deck canvas is only 0.86 m tall and
            // its buttons already fill it, while this strip is pixel-crisp and otherwise empty. It
            // replaces a static usage hint, which carried less information than a live readout.
            context.CaptureStatusLabel = ui.Label(
                "Capture Status",
                canvas.transform,
                new Vector2(halfX - margin - 500f, -halfY + 54f),
                new Vector2(1000f, 44f),
                theme.Data.captureStatusIdle,
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
