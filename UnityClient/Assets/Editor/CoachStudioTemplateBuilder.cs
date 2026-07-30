using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace FpsAiCoach.Editor
{
    public static class CoachStudioTemplateBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MaterialFolder = "Assets/Art/Materials";
        private const string PrefabFolder = "Assets/Prefabs";
        private const float WorldUiScale = 0.0015f;

        private static Material backgroundMaterial;
        private static Material floorMaterial;
        private static Material panelMaterial;
        private static Material panelInsetMaterial;
        private static Material cyanMaterial;
        private static Material blueMaterial;
        private static Material orangeMaterial;
        private static Material whiteMaterial;
        private static TMP_FontAsset uiFontAsset;

        [MenuItem("FPS AI Coach/Create 3D Studio Template")]
        public static void CreateTemplate()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            EnsureFolders();
            CreateMaterials();
            uiFontAsset = CreateUiFontAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("FPS Coach Studio");
            var environment = CreateGroup("Environment", root.transform);
            var architecture = CreateGroup("Architecture", root.transform);
            var workspace = CreateGroup("Coach Workspace", root.transform);
            var lighting = CreateGroup("Lighting", root.transform);

            CreateEnvironment(environment);
            CreateArchitecture(architecture);
            CreateCamera(root.transform);
            CreateEventSystem(root.transform);
            var scanningCore = CreateWorkspace(workspace);
            var statusBeacon = CreateLighting(lighting);

            var view = root.AddComponent<CoachStudioTemplateView>();
            root.AddComponent<WorldButtonRayInteractor>();
            view.Configure(scanningCore, statusBeacon);

            var prefabPath = PrefabFolder + "/FPSCoachStudioTemplate.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Created the FPS Coach Studio 3D template.");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder("Assets/Scenes");
        }

        private static TMP_FontAsset CreateUiFontAsset()
        {
            const string fontPath =
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (fontAsset != null)
                return fontAsset;

            throw new InvalidOperationException(
                "TextMesh Pro Essential Resources are missing. " +
                "Run FPS AI Coach/Import TMP Essential Resources first.");
        }

        [MenuItem("FPS AI Coach/Import TMP Essential Resources")]
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent ?? "Assets", name);
        }

        private static void CreateMaterials()
        {
            backgroundMaterial = CreateMaterial(
                "StudioBackground",
                new Color(0.012f, 0.018f, 0.03f),
                0.05f,
                0.2f);
            floorMaterial = CreateMaterial(
                "StudioFloor",
                new Color(0.025f, 0.035f, 0.055f),
                0.35f,
                0.7f);
            panelMaterial = CreateMaterial(
                "PanelShell",
                new Color(0.035f, 0.055f, 0.085f),
                0.25f,
                0.65f);
            panelInsetMaterial = CreateMaterial(
                "PanelInset",
                new Color(0.012f, 0.022f, 0.04f),
                0.05f,
                0.55f);
            cyanMaterial = CreateMaterial(
                "CoachCyan",
                new Color(0.03f, 0.5f, 0.62f),
                0.15f,
                0.75f,
                new Color(0.03f, 1.1f, 1.35f));
            blueMaterial = CreateMaterial(
                "CoachBlue",
                new Color(0.08f, 0.2f, 0.6f),
                0.1f,
                0.7f,
                new Color(0.08f, 0.3f, 1.15f));
            orangeMaterial = CreateMaterial(
                "CoachOrange",
                new Color(0.8f, 0.22f, 0.04f),
                0.05f,
                0.65f,
                new Color(1.2f, 0.25f, 0.02f));
            whiteMaterial = CreateMaterial(
                "CoachWhite",
                new Color(0.65f, 0.76f, 0.86f),
                0.05f,
                0.55f);
        }

        private static Material CreateMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Color? emission = null)
        {
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group;
        }

        private static void CreateEnvironment(GameObject parent)
        {
            CreatePrimitive(
                "Floor",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(0f, -0.45f, 2.5f),
                new Vector3(18f, 0.5f, 14f),
                floorMaterial);
            CreatePrimitive(
                "Back Wall",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(0f, 4.2f, 8.5f),
                new Vector3(18f, 9f, 0.4f),
                backgroundMaterial);
            CreatePrimitive(
                "Left Wall",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(-9f, 3.2f, 2.5f),
                new Vector3(0.35f, 7f, 12f),
                backgroundMaterial);
            CreatePrimitive(
                "Right Wall",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(9f, 3.2f, 2.5f),
                new Vector3(0.35f, 7f, 12f),
                backgroundMaterial);

            for (var index = -4; index <= 4; index++)
            {
                CreatePrimitive(
                    "Floor Guide " + index,
                    PrimitiveType.Cube,
                    parent.transform,
                    new Vector3(index * 1.8f, -0.17f, 2.5f),
                    new Vector3(0.025f, 0.015f, 11f),
                    cyanMaterial);
            }

            for (var index = 0; index < 7; index++)
            {
                CreatePrimitive(
                    "Depth Guide " + index,
                    PrimitiveType.Cube,
                    parent.transform,
                    new Vector3(0f, -0.16f, -2.5f + index * 1.65f),
                    new Vector3(16f, 0.015f, 0.025f),
                    blueMaterial);
            }
        }

        private static void CreateArchitecture(GameObject parent)
        {
            CreatePrimitive(
                "Raised Platform",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(0f, 0f, 3.6f),
                new Vector3(14.5f, 0.22f, 7.5f),
                panelMaterial);

            CreateFrameColumn(parent.transform, -7.2f);
            CreateFrameColumn(parent.transform, 7.2f);

            var header = CreatePrimitive(
                "Header Beam",
                PrimitiveType.Cube,
                parent.transform,
                new Vector3(0f, 6.4f, 6.9f),
                new Vector3(14.8f, 0.28f, 0.35f),
                panelMaterial);
            CreatePrimitive(
                "Header Accent",
                PrimitiveType.Cube,
                header.transform,
                new Vector3(0f, -0.18f, -0.2f),
                new Vector3(0.92f, 0.06f, 0.08f),
                cyanMaterial,
                true);

            CreateWorldText(
                "Studio Title",
                "FPS COACH // ANALYSIS STUDIO",
                parent.transform,
                new Vector3(-6.7f, 6.05f, 6.65f),
                0.2f,
                Color.white,
                TextAnchor.MiddleLeft);
            CreateWorldText(
                "Studio Status",
                "DEMO MODE // READY",
                parent.transform,
                new Vector3(6.25f, 6.05f, 6.64f),
                0.1f,
                new Color(0.35f, 0.95f, 1f),
                TextAnchor.MiddleRight);
        }

        private static void CreateFrameColumn(Transform parent, float x)
        {
            CreatePrimitive(
                x < 0 ? "Left Frame Column" : "Right Frame Column",
                PrimitiveType.Cube,
                parent,
                new Vector3(x, 3.25f, 6.9f),
                new Vector3(0.3f, 6.4f, 0.4f),
                panelMaterial);
            CreatePrimitive(
                x < 0 ? "Left Column Accent" : "Right Column Accent",
                PrimitiveType.Cube,
                parent,
                new Vector3(x, 3.25f, 6.65f),
                new Vector3(0.08f, 5.8f, 0.08f),
                cyanMaterial);
        }

        private static Transform CreateWorkspace(GameObject parent)
        {
            CreatePanel(
                "Match Library",
                parent.transform,
                new Vector3(-5.7f, 3.25f, 6.5f),
                new Vector2(2.6f, 5.3f),
                "MATCH LIBRARY");
            CreatePanel(
                "Video Review",
                parent.transform,
                new Vector3(-0.65f, 3.25f, 6.5f),
                new Vector2(7.2f, 5.3f),
                "VIDEO REVIEW");
            CreatePanel(
                "Coach Insights",
                parent.transform,
                new Vector3(4.65f, 3.25f, 6.5f),
                new Vector2(3.1f, 5.3f),
                "COACH INSIGHTS");

            CreateMatchLibraryDetails(parent.transform);
            var scanningCore = CreateDemoReviewDetails(parent.transform);
            CreateInsightDetails(parent.transform);
            CreateControlDeck(parent.transform);
            return scanningCore;
        }

        private static void CreatePanel(
            string name,
            Transform parent,
            Vector3 position,
            Vector2 size,
            string title)
        {
            var group = CreateGroup(name, parent);
            group.transform.localPosition = position;
            CreatePrimitive(
                "Shell",
                PrimitiveType.Cube,
                group.transform,
                Vector3.zero,
                new Vector3(size.x, size.y, 0.28f),
                panelMaterial,
                true);
            CreatePrimitive(
                "Inset",
                PrimitiveType.Cube,
                group.transform,
                new Vector3(0f, -0.1f, -0.18f),
                new Vector3(size.x - 0.22f, size.y - 0.55f, 0.08f),
                panelInsetMaterial,
                true);
            CreatePrimitive(
                "Top Accent",
                PrimitiveType.Cube,
                group.transform,
                new Vector3(0f, size.y * 0.5f - 0.18f, -0.22f),
                new Vector3(size.x - 0.18f, 0.08f, 0.06f),
                cyanMaterial,
                true);
            CreateWorldText(
                "Title",
                title,
                group.transform,
                new Vector3(-size.x * 0.5f + 0.18f, size.y * 0.5f - 0.42f, -0.25f),
                0.17f,
                Color.white,
                TextAnchor.MiddleLeft);
        }

        private static void CreateMatchLibraryDetails(Transform parent)
        {
            var origin = new Vector3(-5.7f, 4.55f, 6.25f);
            for (var index = 0; index < 4; index++)
            {
                var y = origin.y - index * 0.85f;
                CreatePrimitive(
                    "Match Card " + (index + 1),
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(origin.x, y, origin.z),
                    new Vector3(2.15f, 0.62f, 0.08f),
                    index == 0 ? blueMaterial : panelMaterial);
                CreatePrimitive(
                    "Match Status " + (index + 1),
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(origin.x - 0.88f, y, origin.z - 0.08f),
                    new Vector3(0.09f, 0.42f, 0.06f),
                    index == 0 ? cyanMaterial : whiteMaterial);
                CreateWorldText(
                    "Match Label " + (index + 1),
                    index == 0 ? "MIRAGE  13:9" : "MATCH  0" + (index + 1),
                    parent,
                    new Vector3(origin.x - 0.7f, y + 0.04f, origin.z - 0.08f),
                    0.14f,
                    Color.white,
                    TextAnchor.MiddleLeft);

                if (index == 0)
                {
                    CreateWorldText(
                        "Selected Match Metadata",
                        "TODAY // 34 MIN",
                        parent,
                        new Vector3(origin.x - 0.7f, y - 0.18f, origin.z - 0.08f),
                        0.075f,
                        new Color(0.62f, 0.86f, 0.95f),
                        TextAnchor.MiddleLeft);
                }
            }
        }

        private static Transform CreateDemoReviewDetails(Transform parent)
        {
            var viewport = CreatePrimitive(
                "Tactical Viewport",
                PrimitiveType.Cube,
                parent,
                new Vector3(-0.65f, 3.75f, 6.22f),
                new Vector3(6.55f, 2.65f, 0.08f),
                backgroundMaterial);

            var replayMarkers = CreateGroup("Replay Markers", parent);
            replayMarkers.transform.localPosition = new Vector3(-0.65f, 3.75f, 6.02f);
            for (var index = 0; index < 10; index++)
            {
                var marker = CreatePrimitive(
                    "Replay Player " + index,
                    PrimitiveType.Sphere,
                    replayMarkers.transform,
                    Vector3.zero,
                    Vector3.one * 0.12f,
                    index < 5 ? cyanMaterial : orangeMaterial,
                    true);
                marker.SetActive(false);
            }

            var core = CreateGroup("Scanning Core", parent);
            core.transform.localPosition = new Vector3(-0.65f, 3.75f, 6.02f);
            CreatePrimitive(
                "Horizontal",
                PrimitiveType.Cube,
                core.transform,
                Vector3.zero,
                new Vector3(0.72f, 0.035f, 0.035f),
                cyanMaterial,
                true);

            CreateWorldText(
                "Demo Status",
                "NO VIDEO",
                parent,
                new Vector3(-3.55f, 4.85f, 6.08f),
                0.1f,
                new Color(0.3f, 0.95f, 1f),
                TextAnchor.MiddleLeft);
            CreateWorldText(
                "Round Status",
                "00:00 // 00:00",
                parent,
                new Vector3(2.25f, 4.85f, 6.08f),
                0.1f,
                Color.white,
                TextAnchor.MiddleRight);
            CreatePrimitive(
                "Vertical",
                PrimitiveType.Cube,
                core.transform,
                Vector3.zero,
                new Vector3(0.035f, 0.72f, 0.035f),
                cyanMaterial,
                true);

            CreatePrimitive(
                "Timeline Track",
                PrimitiveType.Cube,
                parent,
                new Vector3(-0.65f, 1.75f, 6.23f),
                new Vector3(6.4f, 0.12f, 0.08f),
                panelMaterial);
            CreatePrimitive(
                "Timeline Progress",
                PrimitiveType.Cube,
                parent,
                new Vector3(-3.83f, 1.75f, 6.13f),
                new Vector3(0.04f, 0.07f, 0.05f),
                cyanMaterial);

            for (var index = 0; index < 6; index++)
            {
                CreatePrimitive(
                    "Timeline Event " + index,
                    PrimitiveType.Sphere,
                    parent,
                    new Vector3(-3.3f + index * 1.05f, 1.75f, 6.04f),
                    Vector3.one * 0.12f,
                    index == 3 ? orangeMaterial : whiteMaterial);
            }

            return core.transform;
        }

        private static void CreateInsightDetails(Transform parent)
        {
            var statNames = new[] { "AIM", "POSITION", "DECISION" };
            var values = new[] { 0.78f, 0.64f, 0.72f };
            var displayValues = new[] { "78", "64", "72" };
            for (var index = 0; index < statNames.Length; index++)
            {
                var y = 4.65f - index * 0.82f;
                CreateWorldText(
                    statNames[index] + " Label",
                    statNames[index],
                    parent,
                    new Vector3(3.1f, y + 0.18f, 6.2f),
                    0.15f,
                    Color.white,
                    TextAnchor.MiddleLeft);
                CreateWorldText(
                    statNames[index] + " Score",
                    displayValues[index],
                    parent,
                    new Vector3(5.95f, y + 0.18f, 6.08f),
                    0.15f,
                    index == 1
                        ? new Color(0.4f, 0.58f, 1f)
                        : new Color(0.3f, 1f, 1f),
                    TextAnchor.MiddleRight);
                CreatePrimitive(
                    statNames[index] + " Track",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(4.65f, y - 0.08f, 6.22f),
                    new Vector3(2.7f, 0.13f, 0.08f),
                    panelMaterial);
                CreatePrimitive(
                    statNames[index] + " Value",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(3.3f + values[index] * 1.35f, y - 0.08f, 6.12f),
                    new Vector3(2.7f * values[index], 0.08f, 0.05f),
                    index == 1 ? blueMaterial : cyanMaterial);
            }

            CreatePrimitive(
                "Insight Card",
                PrimitiveType.Cube,
                parent,
                new Vector3(4.65f, 1.65f, 6.2f),
                new Vector3(3.15f, 0.9f, 0.08f),
                panelMaterial);
            CreatePrimitive(
                "Insight Priority",
                PrimitiveType.Cube,
                parent,
                new Vector3(3.22f, 1.65f, 6.1f),
                new Vector3(0.1f, 0.68f, 0.05f),
                orangeMaterial);
            CreateWorldText(
                "Insight Priority Label",
                "HIGH PRIORITY",
                parent,
                new Vector3(3.45f, 1.91f, 6.08f),
                0.085f,
                new Color(1f, 0.45f, 0.18f),
                TextAnchor.MiddleLeft);
            CreateWorldText(
                "Insight Text",
                "OPENING DUELS\nReview trade spacing",
                parent,
                new Vector3(3.45f, 1.68f, 6.08f),
                0.115f,
                Color.white,
                TextAnchor.UpperLeft);
        }

        private static void CreateControlDeck(Transform parent)
        {
            var deck = CreatePrimitive(
                "Control Deck",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, 0.75f, 3.2f),
                new Vector3(9.5f, 0.35f, 2.2f),
                panelMaterial);
            deck.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            var labels = new[] { "IMPORT VIDEO", "PLAY", "LIVE MODE" };
            var buttonNames = new[] { "IMPORT VIDEO Button", "PLAY Button", "LIVE MODE Button" };
            var colors = new[]
            {
                new Color(0.08f, 0.25f, 0.95f),
                new Color(0.02f, 0.82f, 0.9f),
                new Color(1f, 0.26f, 0.08f)
            };
            for (var index = 0; index < labels.Length; index++)
            {
                var x = -2.8f + index * 2.8f;
                CreateWorldButton(
                    buttonNames[index],
                    labels[index],
                    parent,
                    new Vector3(x, 1.15f, 2.55f),
                    new Vector2(2.15f, 0.52f),
                    colors[index]);
            }
        }

        private static Transform CreateLighting(GameObject parent)
        {
            var directionalObject = new GameObject("Main Directional Light");
            directionalObject.transform.SetParent(parent.transform, false);
            directionalObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            var directional = directionalObject.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(0.55f, 0.68f, 0.9f);
            directional.intensity = 1.25f;
            directional.shadows = LightShadows.Soft;

            CreatePointLight(
                "Cyan Key Light",
                parent.transform,
                new Vector3(-4f, 5.5f, 1.5f),
                new Color(0.05f, 0.75f, 1f),
                5.5f,
                11f);
            CreatePointLight(
                "Blue Fill Light",
                parent.transform,
                new Vector3(4.5f, 4.5f, 2f),
                new Color(0.08f, 0.2f, 1f),
                4.2f,
                10f);

            var beacon = CreatePrimitive(
                "Status Beacon",
                PrimitiveType.Sphere,
                parent.transform,
                new Vector3(6.7f, 6.05f, 6.5f),
                Vector3.one * 0.22f,
                cyanMaterial);
            var beaconLight = beacon.AddComponent<Light>();
            beaconLight.type = LightType.Point;
            beaconLight.color = new Color(0.05f, 0.95f, 1f);
            beaconLight.range = 3f;
            beaconLight.intensity = 2.2f;
            return beacon.transform;
        }

        private static void CreatePointLight(
            string name,
            Transform parent,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = position;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(0f, 3.9f, -9.8f);
            cameraObject.transform.LookAt(new Vector3(0f, 3f, 5f));

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.005f, 0.008f, 0.016f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.allowHDR = true;

            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateEventSystem(Transform parent)
        {
            var eventSystemObject = new GameObject(
                "Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(parent, false);
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool positionIsLocal = false)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = localScale;
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
            return gameObject;
        }

        private static GameObject CreateWorldText(
            string name,
            string content,
            Transform parent,
            Vector3 localPosition,
            float textHeight,
            Color color,
            TextAnchor anchor)
        {
            textHeight *= 1.7f;
            var lineCount = content.Split('\n').Length;
            var longestLineLength = 1;
            foreach (var line in content.Split('\n'))
                longestLineLength = Mathf.Max(longestLineLength, line.Length);

            var worldWidth = Mathf.Max(0.5f, longestLineLength * textHeight * 0.62f);
            var worldHeight = textHeight * lineCount * 1.25f;
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.transform.localPosition = localPosition;

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(
                worldWidth / WorldUiScale,
                worldHeight / WorldUiScale);
            canvasRect.localScale = Vector3.one * WorldUiScale;
            canvasRect.pivot = GetPivot(anchor);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 8f;

            var textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.font = uiFontAsset;
            text.fontSize = Mathf.Max(48f, textHeight / WorldUiScale * 0.9f);
            text.lineSpacing = 0.9f;
            text.color = color;
            text.alignment = GetTmpAlignment(anchor);
            var isHeading =
                name == "Studio Title" ||
                name == "Studio Status" ||
                name == "Title" ||
                name == "Demo Status" ||
                name == "Round Status" ||
                name == "Insight Priority Label" ||
                name.EndsWith("Score", StringComparison.Ordinal);
            text.fontStyle = isHeading ? FontStyles.Bold : FontStyles.Normal;
            text.characterSpacing = isHeading ? 1.5f : 0f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.outlineColor = new Color32(0, 8, 18, 235);
            text.outlineWidth = 0.12f;
            return canvasObject;
        }

        private static GameObject CreateWorldButton(
            string name,
            string label,
            Transform parent,
            Vector3 localPosition,
            Vector2 worldSize,
            Color baseColor)
        {
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(Image),
                typeof(Button));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.transform.localPosition = localPosition;

            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = worldSize / WorldUiScale;
            rect.localScale = Vector3.one * WorldUiScale;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 20;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 8f;

            var image = canvasObject.GetComponent<Image>();
            image.color = baseColor;
            var button = canvasObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = baseColor,
                highlightedColor = Color.Lerp(baseColor, Color.white, 0.28f),
                pressedColor = Color.Lerp(baseColor, Color.black, 0.25f),
                selectedColor = Color.Lerp(baseColor, Color.white, 0.18f),
                disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            var collider = canvasObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                rect.sizeDelta.x,
                rect.sizeDelta.y,
                20f);

            var highlightObject = new GameObject(
                "Top Highlight",
                typeof(RectTransform),
                typeof(Image));
            highlightObject.transform.SetParent(canvasObject.transform, false);
            var highlightRect = highlightObject.GetComponent<RectTransform>();
            highlightRect.anchorMin = new Vector2(0f, 1f);
            highlightRect.anchorMax = Vector2.one;
            highlightRect.pivot = new Vector2(0.5f, 1f);
            highlightRect.anchoredPosition = Vector2.zero;
            highlightRect.sizeDelta = new Vector2(0f, 10f);
            var highlightImage = highlightObject.GetComponent<Image>();
            highlightImage.color = Color.Lerp(baseColor, Color.white, 0.48f);
            highlightImage.raycastTarget = false;

            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(canvasObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(24f, 8f);
            labelRect.offsetMax = new Vector2(-24f, -8f);

            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.font = uiFontAsset;
            text.fontSize = 180f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.characterSpacing = 2f;
            text.color = Color.white;
            text.enableAutoSizing = true;
            text.fontSizeMin = 80f;
            text.fontSizeMax = 180f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.outlineColor = new Color32(0, 6, 14, 235);
            text.outlineWidth = 0.14f;
            return canvasObject;
        }

        private static TextAlignmentOptions GetTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:
                    return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:
                    return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:
                    return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight:
                    return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:
                    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:
                    return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:
                    return TextAlignmentOptions.BottomRight;
                default:
                    return TextAlignmentOptions.Center;
            }
        }

        private static Vector2 GetPivot(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    return new Vector2(0f, 1f);
                case TextAnchor.UpperCenter:
                    return new Vector2(0.5f, 1f);
                case TextAnchor.UpperRight:
                    return Vector2.one;
                case TextAnchor.MiddleLeft:
                    return new Vector2(0f, 0.5f);
                case TextAnchor.MiddleRight:
                    return new Vector2(1f, 0.5f);
                case TextAnchor.LowerLeft:
                    return Vector2.zero;
                case TextAnchor.LowerCenter:
                    return new Vector2(0.5f, 0f);
                case TextAnchor.LowerRight:
                    return new Vector2(1f, 0f);
                default:
                    return new Vector2(0.5f, 0.5f);
            }
        }
    }
}
