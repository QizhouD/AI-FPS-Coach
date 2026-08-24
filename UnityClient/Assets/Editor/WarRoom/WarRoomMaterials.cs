using UnityEditor;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    /// <summary>
    /// Material library for the war room. Lit surfaces use Standard; every accent line, marker and
    /// beacon uses Unlit/Color so the neon reads at an exact hex value and stays crisp without any
    /// post-processing or bloom.
    /// </summary>
    internal sealed class WarRoomMaterials
    {
        private const string Folder = "Assets/Art/Materials/WarRoom";

        public Material Void { get; private set; }
        public Material Floor { get; private set; }
        public Material PanelGlass { get; private set; }
        public Material PanelEdge { get; private set; }

        /// <summary>
        /// Unlit on purpose: a powered-down display must stay near-black regardless of the room
        /// lights, and the runtime swaps it for Unlit/Texture anyway.
        /// </summary>
        public Material ScreenBase { get; private set; }

        /// <summary>Unlit so the timeline track keeps a constant value against the dark backdrop.</summary>
        public Material TrackBase { get; private set; }

        public Material LineCyan { get; private set; }
        public Material LineCyanDim { get; private set; }
        public Material LineBlue { get; private set; }
        public Material LineAmber { get; private set; }
        public Material LineNeutral { get; private set; }
        public Material FloorRail { get; private set; }
        public Material FloorTick { get; private set; }

        public static WarRoomMaterials Create(WarRoomTheme theme)
        {
            WarRoomAssetUtility.EnsureFolder(Folder);
            var palette = theme.Colors;

            var set = new WarRoomMaterials
            {
                Void = Standard("WR_Void", palette.voidBackdrop, 0.05f, 0.12f),
                Floor = Standard("WR_Floor", palette.floorBase, 0.30f, 0.50f),
                PanelGlass = Standard("WR_PanelGlass", palette.panelGlass, 0.18f, 0.55f),
                PanelEdge = Standard("WR_PanelEdge", palette.panelEdge, 0.40f, 0.42f),
                ScreenBase = Unlit("WR_ScreenBase", palette.screenBase, 1f),
                TrackBase = Unlit("WR_TrackBase", palette.cyanDim, 0.16f),

                LineCyan = Unlit("WR_LineCyan", palette.cyanPrimary, 1f),
                LineCyanDim = Unlit("WR_LineCyanDim", palette.cyanDim, palette.frameDim),
                LineBlue = Unlit("WR_LineBlue", palette.blueElectric, 1f),
                LineAmber = Unlit("WR_LineAmber", palette.amberAlert, 1f),
                LineNeutral = Unlit("WR_LineNeutral", palette.textSecondary, 0.55f),
                FloorRail = Unlit("WR_FloorRail", palette.cyanPrimary, palette.floorGuideDim),
                FloorTick = Unlit("WR_FloorTick", palette.blueElectric, palette.floorGuideDim)
            };

            AssetDatabase.SaveAssets();
            return set;
        }

        private static Material Standard(string name, Color srgb, float metallic, float smoothness)
        {
            var material = LoadOrCreate(name, "Standard");
            material.SetColor("_Color", WarRoomColor.ForMaterial(srgb));
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material Unlit(string name, Color srgb, float intensity)
        {
            var material = LoadOrCreate(name, "Unlit/Color");
            var linear = WarRoomColor.ForMaterial(srgb);
            material.SetColor("_Color", WarRoomColor.Scaled(linear, intensity));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreate(string name, string shaderName)
        {
            var path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Shader '{shaderName}' is unavailable.");

            if (existing != null)
            {
                if (existing.shader != shader)
                    existing.shader = shader;
                return existing;
            }

            var created = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
