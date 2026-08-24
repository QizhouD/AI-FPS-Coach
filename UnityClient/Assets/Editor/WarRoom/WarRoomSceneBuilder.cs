using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace FpsAiCoach.Editor
{
    /// <summary>
    /// Generates the war-room scene from <see cref="WarRoomTheme"/>. The scene is treated as build
    /// output: regenerate it rather than hand-editing, so the theme asset stays the single source of
    /// truth for every coordinate and colour.
    /// </summary>
    public static partial class WarRoomSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string PrefabPath = "Assets/Prefabs/FPSCoachStudioTemplate.prefab";
        private const string FontPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("FPS AI Coach/Build War Room Scene", priority = 0)]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            var theme = LoadOrCreateTheme();
            var font = ResolveFont();

            WarRoomAssetUtility.EnsureFolder("Assets/Prefabs");
            WarRoomAssetUtility.EnsureFolder("Assets/Scenes");

            var materials = WarRoomMaterials.Create(theme);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ApplyRenderSettings(theme);

            var context = new WarRoomBuildContext
            {
                Theme = theme,
                Materials = materials,
                Root = new GameObject("FPS Coach Studio")
            };

            // The camera comes first: every world-space canvas needs it as its event camera.
            context.Camera = BuildCamera(context);
            context.Ui = new WarRoomCanvasKit(theme, font, context.Camera);

            BuildEventSystem(context);
            BuildEnvironment(context);
            BuildStage(context);
            BuildTacticalDisplay(context);
            BuildMatchLibrary(context);
            BuildInsights(context);
            BuildHeader(context);
            BuildControlDeck(context);
            BuildScreenSpaceFrame(context);
            BuildLighting(context);

            WireRuntime(context);

            PrefabUtility.SaveAsPrefabAsset(context.Root, PrefabPath);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.activeGameObject = context.Root;
            SceneView.lastActiveSceneView?.FrameSelected();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "FPS AI Coach: war-room scene rebuilt from " + WarRoomTheme.AssetPath +
                " (" + CountCanvases(context.Root) + " canvases, " +
                CountLights(context.Root) + " lights).");
        }

        [MenuItem("FPS AI Coach/Import TMP Essential Resources", priority = 20)]
        public static void ImportTmpEssentialResources()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(TMP_Text).Assembly);
            if (packageInfo == null)
                throw new InvalidOperationException("The Unity UI package could not be located.");

            var packagePath = Path.Combine(
                packageInfo.resolvedPath,
                "Package Resources",
                "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(packagePath, false);
            Debug.Log("Imported TextMesh Pro Essential Resources.");
        }

        /// <summary>
        /// Removes the material set the previous studio builder generated. Kept as an explicit action
        /// rather than part of <see cref="Build"/>, because deleting assets should never be a silent
        /// side effect of regenerating a scene.
        /// </summary>
        [MenuItem("FPS AI Coach/Clean Legacy Studio Assets", priority = 40)]
        public static void CleanLegacyAssets()
        {
            string[] legacy =
            {
                "Assets/Art/Materials/StudioBackground.mat",
                "Assets/Art/Materials/StudioFloor.mat",
                "Assets/Art/Materials/PanelShell.mat",
                "Assets/Art/Materials/PanelInset.mat",
                "Assets/Art/Materials/CoachCyan.mat",
                "Assets/Art/Materials/CoachBlue.mat",
                "Assets/Art/Materials/CoachOrange.mat",
                "Assets/Art/Materials/CoachWhite.mat",
                "Assets/Art/Materials/WarRoomAlert.mat",
                "Assets/Art/Materials/WarRoomMetal.mat",
                "Assets/Art/Materials/WarRoomHoloGrid.mat",
                "Assets/Art/Materials/WarRoomTactical.mat",
                "Assets/Art/Materials/VideoScreenRT.renderTexture"
            };

            var removed = 0;
            foreach (var path in legacy)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                    continue;

                if (AssetDatabase.DeleteAsset(path))
                    removed++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"FPS AI Coach: removed {removed} legacy studio asset(s).");
        }

        // ------------------------------------------------------------------ setup helpers

        private static WarRoomTheme LoadOrCreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<WarRoomTheme>(WarRoomTheme.AssetPath);
            if (theme != null)
                return theme;

            WarRoomAssetUtility.EnsureFolder("Assets/Art/Config");
            theme = ScriptableObject.CreateInstance<WarRoomTheme>();
            AssetDatabase.CreateAsset(theme, WarRoomTheme.AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("FPS AI Coach: created a default war-room theme at " + WarRoomTheme.AssetPath);
            return theme;
        }

        private static TMP_FontAsset ResolveFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null)
                return font;

            throw new InvalidOperationException(
                "TextMesh Pro Essential Resources are missing. " +
                "Run FPS AI Coach/Import TMP Essential Resources first.");
        }

        /// <summary>
        /// Flat ambient and no skybox: the room must be a controlled near-black void, so nothing
        /// leaks in from a default sky and washes out the panel contrast.
        /// </summary>
        private static void ApplyRenderSettings(WarRoomTheme theme)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = theme.Lights.ambient;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0f;
        }

        private static Camera BuildCamera(WarRoomBuildContext context)
        {
            var rig = context.Theme.Camera;

            var host = new GameObject("Main Camera") { tag = "MainCamera" };
            host.transform.SetParent(context.Root.transform, false);
            host.transform.localPosition = rig.position;
            host.transform.LookAt(rig.lookAt);

            var camera = host.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = rig.background;
            camera.fieldOfView = rig.fieldOfView;
            camera.nearClipPlane = rig.nearClip;
            camera.farClipPlane = rig.farClip;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            host.AddComponent<AudioListener>();
            return camera;
        }

        private static void BuildEventSystem(WarRoomBuildContext context)
        {
            var host = new GameObject(
                "Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            host.transform.SetParent(context.Root.transform, false);
        }

        // ------------------------------------------------------------------ wiring

        /// <summary>
        /// Adds the runtime components and hands them serialized references. Nothing here relies on
        /// <c>FindDeepChild</c>, so the hierarchy can be reorganised without breaking playback.
        /// </summary>
        private static void WireRuntime(WarRoomBuildContext context)
        {
            var root = context.Root;
            var theme = context.Theme;
            var screenMetrics = theme.Screen;

            root.AddComponent<VideoPlayer>();

            var screen = root.AddComponent<TacticalScreenController>();
            SetPrivateField(screen, "surfaceRenderer", context.ScreenSurface);

            var animator = root.AddComponent<StudioAnimator>();
            animator.Configure(
                context.Reticle,
                screenMetrics.reticleRotationSpeed,
                context.Beacon,
                theme.Header.beaconPulseSpeed,
                theme.Header.beaconPulseAmount);

            context.Timeline.Configure(
                context.TimelineProgress,
                context.TimelineEvents,
                screenMetrics.trackWidth,
                screenMetrics.progressMinWidth);

            var demo = root.AddComponent<DemoAnalysisController>();
            demo.Configure(theme);

            var hud = root.AddComponent<StudioHudController>();
            hud.Configure(
                theme,
                screen,
                context.Timeline,
                animator,
                context.Library,
                context.Insights,
                demo);
            hud.BindButtons(context.ImportButton, context.PlayButton, context.LiveButton);
            hud.BindLibraryFooter(
                context.ImportDemoButton,
                context.SampleButton,
                context.DemoStatusLabel);
            hud.BindLabels(
                context.ScreenStatusLabel,
                context.TimecodeLabel,
                context.HeaderModeLabel,
                context.HeaderMatchLabel);

            root.AddComponent<WorldButtonRayInteractor>();
            root.AddComponent<VisionInferenceOverlay>();

            // The marker pool and the event dots stay hidden until a real report populates them.
            if (context.ReplayMarkers != null)
                context.ReplayMarkers.gameObject.SetActive(false);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

            if (field == null)
            {
                Debug.LogError($"WarRoomSceneBuilder: field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }

            field.SetValue(target, value);
        }

        private static int CountCanvases(GameObject root)
        {
            return root.GetComponentsInChildren<Canvas>(true).Length;
        }

        private static int CountLights(GameObject root)
        {
            return root.GetComponentsInChildren<Light>(true).Length;
        }
    }
}
