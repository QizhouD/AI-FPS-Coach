using TMPro;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    public static partial class WarRoomSceneBuilder
    {
        /// <summary>
        /// Shared shell for both side rails: a thin lit backplate with hairline edges carrying a
        /// single world-space canvas. Returns the canvas plus the top edge and width of the content
        /// area in canvas units, so callers only deal with stacking.
        /// </summary>
        private static Canvas BuildRailShell(
            WarRoomBuildContext context,
            string groupName,
            float sign,
            string title,
            out Transform group,
            out float contentTop,
            out float contentWidth)
        {
            var theme = context.Theme;
            var rail = theme.Rail;
            var materials = context.Materials;
            var ui = context.Ui;

            var host = WarRoomGeometry.Group(
                groupName,
                context.Root.transform,
                new Vector3(sign * rail.offsetX, rail.centerY, rail.centerZ));
            group = host.transform;

            var plate = rail.backplateSize;
            WarRoomGeometry.Box(
                "Backplate",
                host.transform,
                new Vector3(0f, 0f, rail.backplateZ),
                new Vector3(plate.x, plate.y, rail.backplateDepth),
                materials.PanelGlass,
                castShadows: false,
                receiveShadows: true);

            WarRoomGeometry.BarX(
                "Top Hairline",
                host.transform,
                new Vector3(0f, plate.y * 0.5f, rail.hairlineZ),
                plate.x,
                rail.hairlineThickness,
                materials.LineCyan);

            WarRoomGeometry.BarX(
                "Bottom Hairline",
                host.transform,
                new Vector3(0f, -plate.y * 0.5f, rail.hairlineZ),
                plate.x,
                rail.hairlineThickness,
                materials.LineCyanDim);

            // Only the outward-facing edge carries a vertical line, which keeps the eye travelling
            // inward toward the main screen.
            WarRoomGeometry.BarY(
                "Outer Hairline",
                host.transform,
                new Vector3(sign * plate.x * 0.5f, 0f, rail.hairlineZ),
                plate.y,
                rail.hairlineThickness,
                materials.LineCyanDim);

            var canvasSize = rail.CanvasSize;
            var canvas = ui.WorldCanvas(
                "Canvas",
                host.transform,
                new Vector3(0f, 0f, rail.canvasZ),
                canvasSize,
                theme.Canvas.sortingOrderRail);

            var widthUnits = ui.U(canvasSize.x);
            var heightUnits = ui.U(canvasSize.y);
            var padding = ui.U(0.07f);
            contentWidth = widthUnits - padding * 2f;

            var headerUnits = ui.U(rail.headerHeight);
            var top = heightUnits * 0.5f;

            ui.Label(
                "Section Label",
                canvas.transform,
                new Vector2(0f, top - headerUnits * 0.5f),
                new Vector2(contentWidth, headerUnits),
                title,
                theme.Text.sectionLabel,
                WarRoomColor.ForUi(theme.Colors.textSecondary),
                TextAlignmentOptions.Left,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            ui.Divider(
                "Section Divider",
                canvas.transform,
                new Vector2(0f, top - headerUnits),
                contentWidth,
                ui.U(0.008f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(theme.Colors.cyanDim), 0.45f));

            contentTop = top - headerUnits - ui.U(0.1f);
            return canvas;
        }

        /// <summary>Left rail: a fixed five-row match list with click-to-select.</summary>
        private static void BuildMatchLibrary(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var rail = theme.Rail;
            var ui = context.Ui;

            var canvas = BuildRailShell(
                context,
                "Match Library",
                -1f,
                theme.Data.libraryTitle,
                out var group,
                out var contentTop,
                out var contentWidth);

            var entries = theme.Data.matches;
            var rows = new MatchLibraryController.Row[entries.Length];
            var rowHeight = ui.U(rail.rowHeight);
            var rowGap = ui.U(rail.rowGap);
            var border = ui.U(0.012f);
            var indicator = ui.U(rail.rowIndicatorWidth);
            var colliderDepth = ui.U(theme.Deck.buttonColliderDepth);

            for (var index = 0; index < entries.Length; index++)
            {
                var centerY = contentTop - rowHeight * 0.5f - index * (rowHeight + rowGap);
                rows[index] = ui.ListRow(
                    $"Row {index:00} {entries[index].map}",
                    canvas.transform,
                    new Vector2(0f, centerY),
                    new Vector2(contentWidth, rowHeight),
                    entries[index],
                    border,
                    indicator,
                    colliderDepth);
            }

            var palette = theme.Colors;
            var controller = group.gameObject.AddComponent<MatchLibraryController>();
            controller.Configure(rows);
            controller.ApplyPalette(
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelGlass), 0.55f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.9f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.textMuted), 0.35f),
                WarRoomColor.ForUi(palette.textBright),
                WarRoomColor.ForUi(palette.textSecondary),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelRaised), 0.92f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.cyanPrimary), 0.9f),
                WarRoomColor.ForUi(palette.cyanPrimary),
                WarRoomColor.ForUi(palette.textPrimary),
                WarRoomColor.ForUi(palette.cyanPrimary));

            controller.Refresh();
            context.Library = controller;
        }

        /// <summary>Right rail: metric bars over insight cards, amber reserved for high priority.</summary>
        private static void BuildInsights(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var rail = theme.Rail;
            var palette = theme.Colors;
            var ui = context.Ui;

            var canvas = BuildRailShell(
                context,
                "Coach Insights",
                1f,
                theme.Data.insightsTitle,
                out var group,
                out var contentTop,
                out var contentWidth);

            var metricEntries = theme.Data.metrics;
            var metrics = new InsightsController.MetricBar[metricEntries.Length];
            var rowHeight = ui.U(rail.metricRowHeight);
            var barWidth = ui.U(rail.metricBarWidth);
            var barHeight = ui.U(rail.metricBarHeight);
            var cursor = contentTop;

            for (var index = 0; index < metricEntries.Length; index++)
            {
                var entry = metricEntries[index];
                var row = ui.Rect(
                    $"Metric {index:00} {entry.label}",
                    canvas.transform,
                    new Vector2(0f, cursor - rowHeight * 0.5f),
                    new Vector2(contentWidth, rowHeight));

                ui.Label(
                    "Label",
                    row,
                    new Vector2(-contentWidth * 0.2f, rowHeight * 0.22f),
                    new Vector2(contentWidth * 0.6f, rowHeight * 0.42f),
                    entry.label,
                    theme.Text.metricLabel,
                    WarRoomColor.ForUi(palette.textSecondary),
                    TextAlignmentOptions.Left,
                    theme.Text.trackingLabel,
                    FontStyles.Bold);

                var valueLabel = ui.Label(
                    "Value",
                    row,
                    new Vector2(contentWidth * 0.3f, rowHeight * 0.18f),
                    new Vector2(contentWidth * 0.4f, rowHeight * 0.5f),
                    "0",
                    theme.Text.metricValue,
                    WarRoomColor.ForUi(palette.textPrimary),
                    TextAlignmentOptions.Right,
                    theme.Text.trackingBody,
                    FontStyles.Bold);

                var trackLeft = new Vector2(-contentWidth * 0.5f, -rowHeight * 0.28f);
                ui.LeftFill(
                    "Track",
                    row,
                    trackLeft,
                    new Vector2(barWidth, barHeight),
                    WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.95f));

                var fill = ui.LeftFill(
                    "Fill",
                    row,
                    trackLeft,
                    new Vector2(barWidth * 0.5f, barHeight),
                    WarRoomColor.ForUi(palette.cyanDim));

                metrics[index] = new InsightsController.MetricBar
                {
                    fill = fill.rectTransform,
                    valueLabel = valueLabel,
                    fillImage = fill
                };

                cursor -= rowHeight;
            }

            cursor -= ui.U(0.1f);
            ui.Divider(
                "Card Divider",
                canvas.transform,
                new Vector2(0f, cursor),
                contentWidth,
                ui.U(0.008f),
                WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.95f));
            cursor -= ui.U(0.1f);

            var cardEntries = theme.Data.insights;
            var cards = new InsightsController.InsightCard[cardEntries.Length];
            var cardHeight = ui.U(rail.cardHeight);
            var cardGap = ui.U(rail.cardGap);
            var cardBorder = ui.U(0.012f);
            var cardIndicator = ui.U(rail.cardIndicatorWidth);

            for (var index = 0; index < cardEntries.Length; index++)
            {
                var entry = cardEntries[index];
                var centerY = cursor - cardHeight * 0.5f - index * (cardHeight + cardGap);

                var card = ui.Panel(
                    $"Card {index:00} {entry.title}",
                    canvas.transform,
                    new Vector2(0f, centerY),
                    new Vector2(contentWidth, cardHeight),
                    WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelEdge), 0.85f));

                ui.Panel(
                    "Background",
                    card.transform,
                    Vector2.zero,
                    new Vector2(contentWidth - cardBorder * 2f, cardHeight - cardBorder * 2f),
                    WarRoomColor.WithAlpha(WarRoomColor.ForUi(palette.panelGlass), 0.75f));

                var indicatorImage = ui.LeftFill(
                    "Indicator",
                    card.transform,
                    new Vector2(-contentWidth * 0.5f + cardBorder, 0f),
                    new Vector2(cardIndicator, cardHeight - cardBorder * 2f),
                    WarRoomColor.ForUi(entry.highPriority ? palette.amberAlert : palette.blueElectric));

                var textLeft = -contentWidth * 0.5f + cardBorder + cardIndicator + ui.U(0.11f);
                var textWidth = contentWidth - (cardBorder + cardIndicator + ui.U(0.11f)) - ui.U(0.1f);

                var titleLabel = ui.Label(
                    "Title",
                    card.transform,
                    new Vector2(textLeft + textWidth * 0.5f, cardHeight * 0.22f),
                    new Vector2(textWidth, cardHeight * 0.4f),
                    entry.title,
                    theme.Text.cardTitle,
                    WarRoomColor.ForUi(palette.textPrimary),
                    TextAlignmentOptions.Left,
                    theme.Text.trackingLabel,
                    FontStyles.Bold);

                var bodyLabel = ui.Label(
                    "Body",
                    card.transform,
                    new Vector2(textLeft + textWidth * 0.5f, -cardHeight * 0.2f),
                    new Vector2(textWidth, cardHeight * 0.45f),
                    entry.body,
                    theme.Text.cardBody,
                    WarRoomColor.ForUi(palette.textSecondary),
                    TextAlignmentOptions.TopLeft,
                    theme.Text.trackingBody,
                    FontStyles.Normal,
                    wrap: true);

                cards[index] = new InsightsController.InsightCard
                {
                    indicator = indicatorImage,
                    titleLabel = titleLabel,
                    bodyLabel = bodyLabel
                };
            }

            var controller = group.gameObject.AddComponent<InsightsController>();
            controller.Configure(metrics, cards, barWidth);
            context.Insights = controller;
        }
    }
}
