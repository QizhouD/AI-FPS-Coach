using TMPro;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    public static partial class WarRoomSceneBuilder
    {
        /// <summary>
        /// The hero region and the only part of the scene that stays fully 3D: a floating tactical
        /// surface with a hairline frame, corner brackets, a status strip, the scanning reticle, the
        /// replay marker pool and the timeline.
        /// </summary>
        private static void BuildTacticalDisplay(WarRoomBuildContext context)
        {
            var theme = context.Theme;
            var metrics = theme.Screen;
            var materials = context.Materials;

            var group = WarRoomGeometry.Group(
                "Tactical Display",
                context.Root.transform,
                metrics.anchor);

            var backplate = metrics.BackplateSize;
            WarRoomGeometry.Box(
                "Screen Backplate",
                group.transform,
                new Vector3(0f, 0f, metrics.backplateZ),
                new Vector3(backplate.x, backplate.y, metrics.backplateDepth),
                materials.PanelEdge,
                castShadows: false,
                receiveShadows: true);

            // Kept named "Tactical Viewport" so external tooling that looks the surface up by name
            // keeps working. The renderer is handed to the controller by reference regardless.
            var surface = WarRoomGeometry.Box(
                "Tactical Viewport",
                group.transform,
                Vector3.zero,
                new Vector3(metrics.width, metrics.Height, metrics.surfaceDepth),
                materials.ScreenBase);
            context.ScreenSurface = surface.GetComponent<Renderer>();

            BuildScreenFrame(context, group.transform);
            BuildReticle(context, group.transform);
            BuildReplayMarkers(context, group.transform);
            BuildStatusStrip(context, group.transform);
            BuildTimeline(context, group.transform);
        }

        private static void BuildScreenFrame(WarRoomBuildContext context, Transform parent)
        {
            var metrics = context.Theme.Screen;
            var materials = context.Materials;
            var halfWidth = metrics.FrameHalfWidth;
            var halfHeight = metrics.FrameHalfHeight;

            var frame = WarRoomGeometry.Group("Screen Frame", parent);

            WarRoomGeometry.BarX(
                "Frame Top",
                frame.transform,
                new Vector3(0f, halfHeight, metrics.frameZ),
                halfWidth * 2f,
                metrics.frameThickness,
                materials.LineCyanDim);

            WarRoomGeometry.BarX(
                "Frame Bottom",
                frame.transform,
                new Vector3(0f, -halfHeight, metrics.frameZ),
                halfWidth * 2f,
                metrics.frameThickness,
                materials.LineCyanDim);

            WarRoomGeometry.BarY(
                "Frame Left",
                frame.transform,
                new Vector3(-halfWidth, 0f, metrics.frameZ),
                halfHeight * 2f,
                metrics.frameThickness,
                materials.LineCyanDim);

            WarRoomGeometry.BarY(
                "Frame Right",
                frame.transform,
                new Vector3(halfWidth, 0f, metrics.frameZ),
                halfHeight * 2f,
                metrics.frameThickness,
                materials.LineCyanDim);

            // Bright corner brackets over a dim continuous frame is what gives the surface its sharp
            // cut-corner read without resorting to a glowing outline.
            var brackets = WarRoomGeometry.Group("Corner Brackets", parent);
            var length = metrics.bracketLength;
            var thickness = metrics.bracketThickness;

            for (var corner = 0; corner < 4; corner++)
            {
                var signX = (corner & 1) == 0 ? -1f : 1f;
                var signY = (corner & 2) == 0 ? -1f : 1f;
                var label = $"{(signY < 0f ? "Bottom" : "Top")} {(signX < 0f ? "Left" : "Right")}";

                WarRoomGeometry.BarX(
                    $"Bracket {label} H",
                    brackets.transform,
                    new Vector3(
                        signX * (halfWidth - length * 0.5f),
                        signY * halfHeight,
                        metrics.frameZ),
                    length,
                    thickness,
                    materials.LineCyan);

                WarRoomGeometry.BarY(
                    $"Bracket {label} V",
                    brackets.transform,
                    new Vector3(
                        signX * halfWidth,
                        signY * (halfHeight - length * 0.5f),
                        metrics.frameZ),
                    length,
                    thickness,
                    materials.LineCyan);
            }
        }

        private static void BuildReticle(WarRoomBuildContext context, Transform parent)
        {
            var metrics = context.Theme.Screen;
            var materials = context.Materials;

            var reticle = WarRoomGeometry.Group(
                "Scanning Reticle",
                parent,
                new Vector3(0f, 0f, metrics.reticleZ));

            WarRoomGeometry.BarX(
                "Arm H",
                reticle.transform,
                Vector3.zero,
                metrics.reticleArmLength,
                metrics.reticleThickness,
                materials.LineCyan);

            WarRoomGeometry.BarY(
                "Arm V",
                reticle.transform,
                Vector3.zero,
                metrics.reticleArmLength,
                metrics.reticleThickness,
                materials.LineCyan);

            context.Reticle = reticle.transform;
        }

        private static void BuildReplayMarkers(WarRoomBuildContext context, Transform parent)
        {
            var metrics = context.Theme.Screen;
            var materials = context.Materials;

            var pool = WarRoomGeometry.Group(
                "Replay Markers",
                parent,
                new Vector3(0f, 0f, metrics.markerZ));

            for (var index = 0; index < metrics.markerCount; index++)
            {
                // Cyan and blue distinguish the two sides. Amber is reserved for priority only.
                var material = index < metrics.markerCount / 2
                    ? materials.LineCyan
                    : materials.LineBlue;

                var marker = WarRoomGeometry.Sphere(
                    $"Marker {index:00}",
                    pool.transform,
                    Vector3.zero,
                    metrics.markerDiameter,
                    material);
                marker.SetActive(false);
            }

            context.ReplayMarkers = pool.transform;
        }

        private static void BuildStatusStrip(WarRoomBuildContext context, Transform parent)
        {
            var theme = context.Theme;
            var metrics = theme.Screen;
            var materials = context.Materials;
            var ui = context.Ui;
            var width = metrics.FrameHalfWidth * 2f;

            var strip = WarRoomGeometry.Group(
                "Status Strip",
                parent,
                new Vector3(0f, metrics.StatusStripY, 0f));

            WarRoomGeometry.Box(
                "Strip Base",
                strip.transform,
                new Vector3(0f, 0f, 0.02f),
                new Vector3(width, metrics.statusStripHeight, metrics.statusStripDepth),
                materials.PanelGlass,
                castShadows: false,
                receiveShadows: true);

            WarRoomGeometry.BarX(
                "Strip Hairline",
                strip.transform,
                new Vector3(0f, -metrics.statusStripHeight * 0.5f, -0.05f),
                width,
                metrics.frameThickness,
                materials.LineCyanDim);

            var canvasWidth = width - 0.2f;
            var canvas = ui.WorldCanvas(
                "Status Canvas",
                strip.transform,
                new Vector3(0f, 0f, -0.06f),
                new Vector2(canvasWidth, metrics.statusStripHeight),
                theme.Canvas.sortingOrderScreen);

            var units = ui.U(canvasWidth);
            var height = ui.U(metrics.statusStripHeight);
            var padding = ui.U(0.06f);

            ui.Label(
                "Section Label",
                canvas.transform,
                new Vector2(-units * 0.5f + padding + units * 0.2f, 0f),
                new Vector2(units * 0.4f, height),
                theme.Data.screenTitle,
                theme.Text.sectionLabel,
                WarRoomColor.ForUi(theme.Colors.textSecondary),
                TextAlignmentOptions.Left,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            context.ScreenStatusLabel = ui.Label(
                "Screen Status",
                canvas.transform,
                Vector2.zero,
                new Vector2(units * 0.4f, height),
                theme.Data.statusNoVideo,
                theme.Text.screenStatus,
                WarRoomColor.ForUi(theme.Colors.cyanPrimary),
                TextAlignmentOptions.Center,
                theme.Text.trackingLabel,
                FontStyles.Bold);

            context.TimecodeLabel = ui.Label(
                "Timecode",
                canvas.transform,
                new Vector2(units * 0.5f - padding - units * 0.18f, 0f),
                new Vector2(units * 0.36f, height),
                "00:00  //  00:00",
                theme.Text.timecode,
                WarRoomColor.ForUi(theme.Colors.textBright),
                TextAlignmentOptions.Right,
                theme.Text.trackingBody);
        }

        private static void BuildTimeline(WarRoomBuildContext context, Transform parent)
        {
            var metrics = context.Theme.Screen;
            var materials = context.Materials;

            var timeline = WarRoomGeometry.Group(
                "Timeline",
                parent,
                new Vector3(0f, metrics.TimelineY, 0f));

            WarRoomGeometry.Box(
                "Track",
                timeline.transform,
                new Vector3(0f, 0f, 0.02f),
                new Vector3(metrics.trackWidth, metrics.trackHeight, metrics.trackDepth),
                materials.TrackBase);

            var progress = WarRoomGeometry.Box(
                "Progress",
                timeline.transform,
                new Vector3(
                    -metrics.trackWidth * 0.5f + metrics.progressMinWidth * 0.5f,
                    0f,
                    -0.05f),
                new Vector3(metrics.progressMinWidth, metrics.progressHeight, 0.05f),
                materials.LineCyan);

            var events = WarRoomGeometry.Group(
                "Events",
                timeline.transform,
                new Vector3(0f, 0f, -0.07f));

            var origin = (metrics.eventCount - 1) * 0.5f;
            for (var index = 0; index < metrics.eventCount; index++)
            {
                var material = index == metrics.highPriorityEventIndex
                    ? materials.LineAmber
                    : materials.LineNeutral;

                WarRoomGeometry.Sphere(
                    $"Event {index:00}",
                    events.transform,
                    new Vector3((index - origin) * metrics.eventSpacing, 0f, 0f),
                    metrics.eventDiameter,
                    material);
            }

            context.Timeline = timeline.AddComponent<TimelineController>();
            context.TimelineProgress = progress.transform;
            context.TimelineEvents = events.transform;
        }
    }
}
