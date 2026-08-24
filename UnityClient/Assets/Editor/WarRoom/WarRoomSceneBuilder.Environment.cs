using UnityEngine;

namespace FpsAiCoach.Editor
{
    public static partial class WarRoomSceneBuilder
    {
        /// <summary>
        /// The enclosing void plus the faint floor guides. Guides are deliberately sparse and heavily
        /// dimmed so they read as survey lines rather than decoration.
        /// </summary>
        private static void BuildEnvironment(WarRoomBuildContext context)
        {
            var room = context.Theme.RoomMetrics;
            var materials = context.Materials;
            var group = WarRoomGeometry.Group("Environment", context.Root.transform);

            WarRoomGeometry.Box(
                "Floor",
                group.transform,
                room.floorCenter,
                room.floorSize,
                materials.Floor,
                castShadows: false,
                receiveShadows: true);

            WarRoomGeometry.Box(
                "Wall Back",
                group.transform,
                room.backWallCenter,
                room.backWallSize,
                materials.Void,
                castShadows: false,
                receiveShadows: true);

            WarRoomGeometry.Box(
                "Wall Left",
                group.transform,
                new Vector3(-room.sideWallX, room.sideWallCenter.y, room.sideWallCenter.z),
                room.sideWallSize,
                materials.Void,
                castShadows: false,
                receiveShadows: true);

            WarRoomGeometry.Box(
                "Wall Right",
                group.transform,
                new Vector3(room.sideWallX, room.sideWallCenter.y, room.sideWallCenter.z),
                room.sideWallSize,
                materials.Void,
                castShadows: false,
                receiveShadows: true);

            var guides = WarRoomGeometry.Group("Floor Guides", group.transform);

            var railOrigin = (room.railCount - 1) * 0.5f;
            for (var index = 0; index < room.railCount; index++)
            {
                WarRoomGeometry.BarZ(
                    $"Rail {index}",
                    guides.transform,
                    new Vector3((index - railOrigin) * room.railSpacing, room.railY, room.railZ),
                    room.railLength,
                    room.railThickness,
                    materials.FloorRail);
            }

            for (var index = 0; index < room.tickCount; index++)
            {
                WarRoomGeometry.BarX(
                    $"Tick {index}",
                    guides.transform,
                    new Vector3(0f, room.tickY, room.tickZStart + index * room.tickSpacing),
                    room.tickLength,
                    room.tickThickness,
                    materials.FloorTick);
            }

            WarRoomGeometry.MarkStaticRecursive(group);
        }

        /// <summary>Raised platform, side frames and the header beam that anchors the composition.</summary>
        private static void BuildStage(WarRoomBuildContext context)
        {
            var room = context.Theme.RoomMetrics;
            var materials = context.Materials;
            var group = WarRoomGeometry.Group("Stage", context.Root.transform);

            WarRoomGeometry.Box(
                "Platform",
                group.transform,
                room.platformCenter,
                room.platformSize,
                materials.PanelEdge,
                castShadows: true,
                receiveShadows: true);

            var platformTop = room.platformCenter.y + room.platformSize.y * 0.5f;
            var platformFront = room.platformCenter.z - room.platformSize.z * 0.5f;
            WarRoomGeometry.BarX(
                "Platform Edge",
                group.transform,
                new Vector3(0f, platformTop, platformFront),
                room.platformSize.x,
                room.platformEdgeThickness,
                materials.LineCyanDim);

            for (var side = 0; side < 2; side++)
            {
                var sign = side == 0 ? -1f : 1f;
                var label = side == 0 ? "Left" : "Right";

                WarRoomGeometry.Box(
                    $"Frame Column {label}",
                    group.transform,
                    new Vector3(sign * room.columnX, room.columnY, room.columnZ),
                    room.columnSize,
                    materials.PanelEdge,
                    castShadows: true,
                    receiveShadows: true);

                WarRoomGeometry.BarY(
                    $"Column Accent {label}",
                    group.transform,
                    new Vector3(
                        sign * room.columnX,
                        room.columnY,
                        room.columnZ - room.columnAccentInset),
                    room.columnSize.y - 0.6f,
                    room.columnAccentThickness,
                    materials.LineCyan);
            }

            WarRoomGeometry.Box(
                "Header Beam",
                group.transform,
                new Vector3(0f, room.beamY, room.beamZ),
                room.beamSize,
                materials.PanelEdge,
                castShadows: true,
                receiveShadows: true);

            WarRoomGeometry.BarX(
                "Beam Hairline",
                group.transform,
                new Vector3(
                    0f,
                    room.beamY - room.beamHairlineDrop,
                    room.beamZ - room.columnAccentInset),
                room.beamHairlineLength,
                room.platformEdgeThickness,
                materials.LineCyan);

            WarRoomGeometry.MarkStaticRecursive(group);
        }

        /// <summary>
        /// Three lights total: a cool directional key that is the only shadow caster, plus two
        /// unshadowed point lights that supply the cyan and blue wash.
        /// </summary>
        private static void BuildLighting(WarRoomBuildContext context)
        {
            var rig = context.Theme.Lights;
            var group = WarRoomGeometry.Group("Lighting", context.Root.transform);

            var keyHost = new GameObject("Key Light");
            keyHost.transform.SetParent(group.transform, false);
            keyHost.transform.localRotation = Quaternion.Euler(rig.keyRotation);

            var key = keyHost.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = rig.keyColor;
            key.intensity = rig.keyIntensity;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.72f;

            CreatePointLight(
                "Cyan Accent",
                group.transform,
                rig.accentPosition,
                rig.accentColor,
                rig.accentIntensity,
                rig.accentRange);

            CreatePointLight(
                "Blue Fill",
                group.transform,
                rig.fillPosition,
                rig.fillColor,
                rig.fillIntensity,
                rig.fillRange);
        }

        private static void CreatePointLight(
            string name,
            Transform parent,
            Vector3 localPosition,
            Color color,
            float intensity,
            float range)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = localPosition;

            var light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;

            // Point-light shadows are the most expensive option here and add nothing to a room made
            // of flat panels, so only the directional key casts.
            light.shadows = LightShadows.None;
        }
    }
}
