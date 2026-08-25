using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FpsAiCoach
{
    public sealed class CoachLiveApp : MonoBehaviour
    {
        private enum AppMode
        {
            Demo,
            Live
        }

        private const string EndpointKey = "fps-ai-coach.endpoint";
        private const string DemoModeKey = "fps-ai-coach.demo-mode";

        private readonly Color background = new Color(0.025f, 0.035f, 0.06f);
        private readonly Color panel = new Color(0.055f, 0.07f, 0.11f);
        private readonly Color cyan = new Color(0.16f, 0.88f, 0.91f);
        private readonly Color muted = new Color(0.55f, 0.62f, 0.70f);

        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;
        private GUIStyle scoreStyle;
        private GUIStyle tipStyle;
        [SerializeField] private bool showLegacyGui;
        private Texture2D solidTexture;
        private WebCamTexture source;
        private string[] devices = Array.Empty<string>();
        private int selectedDevice;
        private bool analysisEnabled;
        private bool demoMode = true;
        private bool requestInFlight;
        private float nextAnalysisAt;
        private string endpoint = "http://127.0.0.1:8000/api/v1/analyze/frame";
        private string connectionState = "Not started";
        private string lastAnalysisAt = "--:--:--";
        private AnalysisResponse analysis = new AnalysisResponse();
        private Color32[] previousSamples;
        private AppMode appMode = AppMode.Demo;
        private string demoEndpoint = "http://127.0.0.1:8000/api/v1/analyze/demo";
        private string demoPath = "";
        private string targetPlayer = "";
        private string demoStatus = "Select a CS2 .dem file or load the sample report";
        private bool demoRequestInFlight;
        private DemoAnalysisResponse demoResult;

        /// <summary>
        /// Bootstrapped before the first scene loads, so the war-room HUD can always reach the
        /// capture pipeline without holding a serialized reference across scenes.
        /// </summary>
        public static CoachLiveApp Instance { get; private set; }

        /// <summary>Texture of the running capture device, or null when capture is stopped.</summary>
        public Texture LiveTexture => source;

        /// <summary>Name of the active capture device, for the status readout.</summary>
        public string LiveDeviceName =>
            source != null && devices.Length > 0 ? devices[selectedDevice] : string.Empty;

        public bool IsLiveSourceActive => source != null && source.isPlaying;

        public string ConnectionState => connectionState;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<CoachLiveApp>() != null)
                return;

            var host = new GameObject("FPS AI Coach Live");
            DontDestroyOnLoad(host);
            host.AddComponent<CoachLiveApp>();
        }

        private void Awake()
        {
            Application.runInBackground = true;
            endpoint = PlayerPrefs.GetString(EndpointKey, endpoint);
            demoMode = PlayerPrefs.GetInt(DemoModeKey, 1) == 1;
            RefreshDevices();
        }

        /// <summary>
        /// The singleton is republished here rather than in Awake because a domain reload clears static
        /// fields while leaving this object alive, and Awake does not run again. Callers reaching for
        /// <see cref="Instance"/> would otherwise find null for the rest of the session.
        /// </summary>
        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnDestroy()
        {
            StopSource();
            if (solidTexture != null)
                Destroy(solidTexture);
        }

        /// <summary>Starts capture. Returns false when no device is available.</summary>
        public bool TryStartLiveSource()
        {
            StartSource();
            return source != null;
        }

        public void StopLiveSource()
        {
            StopSource();
        }

        private void RefreshDevices()
        {
            var available = WebCamTexture.devices;
            devices = new string[available.Length];
            for (var i = 0; i < available.Length; i++)
                devices[i] = available[i].name;

            selectedDevice = Mathf.Clamp(selectedDevice, 0, Mathf.Max(0, devices.Length - 1));
            connectionState = devices.Length == 0
                ? "No video device found. Start OBS Virtual Camera."
                : $"Found {devices.Length} video source(s)";
        }

        private void StartSource()
        {
            if (devices.Length == 0)
            {
                RefreshDevices();
                if (devices.Length == 0)
                    return;
            }

            StopSource();
            source = new WebCamTexture(devices[selectedDevice], 1280, 720, 30);
            source.Play();
            connectionState = $"Connecting to {devices[selectedDevice]}";
            analysisEnabled = true;
            nextAnalysisAt = Time.unscaledTime + 1f;
        }

        private void StopSource()
        {
            analysisEnabled = false;
            if (source == null)
                return;

            if (source.isPlaying)
                source.Stop();
            Destroy(source);
            source = null;
            connectionState = "Video source stopped";
        }

        private void Update()
        {
            if (source != null && source.isPlaying && source.width > 32)
                connectionState = $"Live | {source.width}x{source.height}";

            if (!analysisEnabled || requestInFlight || source == null || !source.isPlaying ||
                source.width <= 32 || Time.unscaledTime < nextAnalysisAt)
                return;

            nextAnalysisAt = Time.unscaledTime + 2.5f;
            StartCoroutine(AnalyzeCurrentFrame());
        }

        private IEnumerator AnalyzeCurrentFrame()
        {
            requestInFlight = true;
            var frame = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            frame.SetPixels32(source.GetPixels32());
            frame.Apply(false, false);

            if (demoMode)
            {
                analysis = AnalyzeLocally(frame);
                lastAnalysisAt = DateTime.Now.ToString("HH:mm:ss");
                Destroy(frame);
                requestInFlight = false;
                yield break;
            }

            var jpg = frame.EncodeToJPG(70);
            Destroy(frame);
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("frame", jpg, "frame.jpg", "image/jpeg"),
                new MultipartFormDataSection("game", "auto"),
                new MultipartFormDataSection("session_id", SystemInfo.deviceUniqueIdentifier)
            };

            using (var request = UnityWebRequest.Post(endpoint, form))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var parsed = JsonUtility.FromJson<AnalysisResponse>(request.downloadHandler.text);
                    if (parsed != null && parsed.tip != null && parsed.scores != null)
                    {
                        analysis = parsed;
                        connectionState = "Live | AI service connected";
                        lastAnalysisAt = DateTime.Now.ToString("HH:mm:ss");
                    }
                }
                else
                {
                    analysis.tip.severity = "warning";
                    analysis.tip.title = "AI service unavailable";
                    analysis.tip.message = request.error;
                    analysis.tip.action = "Start the backend or switch to Local Demo mode.";
                    connectionState = "Live | AI service offline";
                }
            }

            requestInFlight = false;
        }

        private IEnumerator LoadSampleDemo()
        {
            demoRequestInFlight = true;
            demoStatus = "Loading sample report...";
            using (var request = UnityWebRequest.Get(demoEndpoint + "/sample"))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                ApplyDemoRequest(request);
            }
            demoRequestInFlight = false;
        }

        private IEnumerator AnalyzeDemoFile()
        {
            demoRequestInFlight = true;
            if (!File.Exists(demoPath))
            {
                demoStatus = "The selected file does not exist.";
                demoRequestInFlight = false;
                yield break;
            }

            var fileInfo = new FileInfo(demoPath);
            if (!fileInfo.Extension.Equals(".dem", StringComparison.OrdinalIgnoreCase))
            {
                demoStatus = "Only CS2 .dem files are supported.";
                demoRequestInFlight = false;
                yield break;
            }
            if (fileInfo.Length > 512L * 1024L * 1024L)
            {
                demoStatus = "The Unity MVP currently accepts demo files up to 512 MB.";
                demoRequestInFlight = false;
                yield break;
            }

            demoStatus = $"Reading and analyzing {fileInfo.Name}...";
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(demoPath);
            }
            catch (Exception exception)
            {
                demoStatus = $"Read failed: {exception.Message}";
                demoRequestInFlight = false;
                yield break;
            }

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("demo", bytes, fileInfo.Name, "application/octet-stream"),
                new MultipartFormDataSection("target_player", targetPlayer.Trim())
            };
            using (var request = UnityWebRequest.Post(demoEndpoint, form))
            {
                request.timeout = 180;
                yield return request.SendWebRequest();
                ApplyDemoRequest(request);
            }
            demoRequestInFlight = false;
        }

        private void ApplyDemoRequest(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                var parsed = JsonUtility.FromJson<DemoAnalysisResponse>(request.downloadHandler.text);
                if (parsed != null && parsed.player != null)
                {
                    demoResult = parsed;
                    demoStatus = $"Analysis complete | {parsed.map_name} | {parsed.rounds} rounds";
                    return;
                }
                demoStatus = "The service returned an invalid report.";
                return;
            }

            var detail = request.downloadHandler != null && !string.IsNullOrWhiteSpace(request.downloadHandler.text)
                ? request.downloadHandler.text
                : request.error;
            demoStatus = $"Analysis failed: {detail}";
        }

        private AnalysisResponse AnalyzeLocally(Texture2D frame)
        {
            const int gridX = 24;
            const int gridY = 14;
            var samples = new Color32[gridX * gridY];
            double luminance = 0;
            double motion = 0;
            var index = 0;

            for (var y = 0; y < gridY; y++)
            {
                for (var x = 0; x < gridX; x++)
                {
                    var pixel = frame.GetPixel(
                        Mathf.Clamp((x * frame.width) / gridX, 0, frame.width - 1),
                        Mathf.Clamp((y * frame.height) / gridY, 0, frame.height - 1));
                    var sample = (Color32)pixel;
                    samples[index] = sample;
                    luminance += (sample.r * 0.2126 + sample.g * 0.7152 + sample.b * 0.0722) / 255.0;
                    if (previousSamples != null)
                    {
                        var previous = previousSamples[index];
                        motion += (Math.Abs(sample.r - previous.r) + Math.Abs(sample.g - previous.g) +
                                   Math.Abs(sample.b - previous.b)) / (255.0 * 3.0);
                    }
                    index++;
                }
            }

            luminance /= samples.Length;
            motion = previousSamples == null ? 0 : motion / samples.Length;
            previousSamples = samples;

            var response = new AnalysisResponse
            {
                session_id = "local-demo",
                timestamp = DateTime.UtcNow.ToString("O")
            };
            response.scores.aim = Mathf.RoundToInt(Mathf.Lerp(62, 82, (float)(1.0 - Math.Min(motion * 3.0, 1.0))));
            response.scores.positioning = Mathf.RoundToInt(Mathf.Lerp(66, 78, (float)luminance));
            response.scores.decision = Mathf.RoundToInt(Mathf.Lerp(64, 80, (float)(1.0 - Math.Min(motion * 2.0, 1.0))));
            response.scores.consistency = Mathf.RoundToInt((response.scores.aim + response.scores.decision) * 0.5f);

            if (luminance < 0.14)
            {
                response.tip.severity = "warning";
                response.tip.title = "Low visibility in dark areas";
                response.tip.message = "Dark areas may hide enemy silhouettes and crosshair placement.";
                response.tip.action = "Check game brightness and the OBS color range.";
            }
            else if (motion > 0.18)
            {
                response.tip.severity = "danger";
                response.tip.title = "Camera movement is too fast";
                response.tip.message = "Continuous large movements reduce visual confirmation and first-shot accuracy.";
                response.tip.action = "Pause briefly and place the crosshair at likely head level before entry.";
            }
            else
            {
                response.tip.severity = "good";
                response.tip.title = "Stable pacing";
                response.tip.message = "Camera movement is stable enough for information gathering and pre-aiming.";
                response.tip.action = "Measure the time from first enemy visibility to the next shot.";
            }
            return response;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            solidTexture = new Texture2D(1, 1);
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply();
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            headingStyle.normal.textColor = Color.white;
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            bodyStyle.normal.textColor = new Color(0.88f, 0.91f, 0.95f);
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 12 };
            smallStyle.normal.textColor = muted;
            scoreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            scoreStyle.normal.textColor = cyan;
            tipStyle = new GUIStyle(bodyStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
        }

        private void DrawBox(Rect rect, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = oldColor;
        }

        private void OnGUI()
        {
            if (!showLegacyGui)
                return;

            EnsureStyles();
            DrawBox(new Rect(0, 0, Screen.width, Screen.height), background);
            var margin = Mathf.Max(16, Screen.width * 0.018f);
            var headerHeight = 72f;
            var sidebarWidth = Mathf.Clamp(Screen.width * 0.29f, 330f, 430f);
            var contentTop = margin + headerHeight;
            var contentHeight = Screen.height - contentTop - margin;
            var previewRect = new Rect(margin, contentTop, Screen.width - sidebarWidth - margin * 3, contentHeight);
            var sidebarRect = new Rect(previewRect.xMax + margin, contentTop, sidebarWidth, contentHeight);

            GUI.Label(new Rect(margin, margin, 360, 36),
                appMode == AppMode.Demo ? "FPS AI COACH  /  DEMO" : "FPS AI COACH  /  LIVE", titleStyle);
            GUI.Label(new Rect(margin, margin + 35, 720, 22),
                appMode == AppMode.Demo ? demoStatus : connectionState, smallStyle);

            var tabX = margin + 360;
            if (GUI.Button(new Rect(tabX, margin, 92, 34), "Demo Analysis"))
                appMode = AppMode.Demo;
            if (GUI.Button(new Rect(tabX + 100, margin, 92, 34), "Live Mode"))
                appMode = AppMode.Live;

            if (appMode == AppMode.Demo)
            {
                DrawDemoWorkspace(margin, contentTop, contentHeight);
                return;
            }

            var buttonWidth = 118f;
            if (GUI.Button(new Rect(Screen.width - margin - buttonWidth, margin, buttonWidth, 36),
                    source == null ? "Start Source" : "Stop Source"))
            {
                if (source == null) StartSource(); else StopSource();
            }

            DrawBox(previewRect, Color.black);
            if (source != null && source.isPlaying && source.width > 32)
            {
                var oldMatrix = GUI.matrix;
                if (source.videoVerticallyMirrored)
                {
                    GUIUtility.ScaleAroundPivot(new Vector2(1, -1),
                        new Vector2(previewRect.center.x, previewRect.center.y));
                }
                GUI.DrawTexture(previewRect, source, ScaleMode.ScaleToFit, false);
                GUI.matrix = oldMatrix;
            }
            else
            {
                var empty = new GUIStyle(headingStyle) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(previewRect,
                    "Waiting for a video source\n\nStart OBS Virtual Camera, then select it from the source list.",
                    empty);
            }

            GUILayout.BeginArea(new Rect(sidebarRect.x + 16, sidebarRect.y + 14,
                sidebarRect.width - 32, sidebarRect.height - 28));
            DrawBox(new Rect(-16, -14, sidebarRect.width, sidebarRect.height), panel);

            GUILayout.Label("Video Source", headingStyle);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(38)) && devices.Length > 0)
                selectedDevice = (selectedDevice - 1 + devices.Length) % devices.Length;
            GUILayout.Label(devices.Length == 0 ? "No device found" : devices[selectedDevice], bodyStyle,
                GUILayout.Height(34));
            if (GUILayout.Button(">", GUILayout.Width(38)) && devices.Length > 0)
                selectedDevice = (selectedDevice + 1) % devices.Length;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Refresh Devices"))
                RefreshDevices();

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Analysis Mode", headingStyle);
            var newDemoMode = GUILayout.Toggle(demoMode, "Local Demo", GUILayout.Width(100));
            GUILayout.EndHorizontal();
            if (newDemoMode != demoMode)
            {
                demoMode = newDemoMode;
                PlayerPrefs.SetInt(DemoModeKey, demoMode ? 1 : 0);
                PlayerPrefs.Save();
            }

            if (!demoMode)
            {
                GUILayout.Label("AI Service URL", smallStyle);
                var updated = GUILayout.TextField(endpoint);
                if (updated != endpoint)
                {
                    endpoint = updated;
                    PlayerPrefs.SetString(EndpointKey, endpoint);
                    PlayerPrefs.Save();
                }
            }

            GUILayout.Space(16);
            GUILayout.Label("Live Skill Scores", headingStyle);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            DrawScore("Aim", analysis.scores.aim);
            DrawScore("Positioning", analysis.scores.positioning);
            DrawScore("Decision", analysis.scores.decision);
            GUILayout.EndHorizontal();

            GUILayout.Space(18);
            var severityColor = analysis.tip.severity == "danger"
                ? new Color(1f, 0.32f, 0.32f)
                : analysis.tip.severity == "warning"
                    ? new Color(1f, 0.72f, 0.24f)
                    : cyan;
            var old = tipStyle.normal.textColor;
            tipStyle.normal.textColor = severityColor;
            GUILayout.Label(analysis.tip.title, tipStyle);
            tipStyle.normal.textColor = old;
            GUILayout.Space(5);
            GUILayout.Label(analysis.tip.message, bodyStyle, GUILayout.MinHeight(48));
            GUILayout.Space(7);
            GUILayout.Label("Next Action", smallStyle);
            GUILayout.Label(analysis.tip.action, bodyStyle, GUILayout.MinHeight(48));

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Frame interval: 2.5s | Last analysis: {lastAnalysisAt}", smallStyle);
            GUILayout.Label("Frames are not stored. Remote mode uploads sampled frames only.", smallStyle);
            GUILayout.EndArea();
        }

        private void DrawDemoWorkspace(float margin, float contentTop, float contentHeight)
        {
            var leftWidth = Mathf.Clamp(Screen.width * 0.34f, 360f, 500f);
            var leftRect = new Rect(margin, contentTop, leftWidth, contentHeight);
            var rightRect = new Rect(leftRect.xMax + margin, contentTop,
                Screen.width - leftRect.xMax - margin * 2, contentHeight);
            DrawBox(leftRect, panel);
            DrawBox(rightRect, panel);

            GUILayout.BeginArea(new Rect(leftRect.x + 18, leftRect.y + 16,
                leftRect.width - 36, leftRect.height - 32));
            GUILayout.Label("CS2 Demo Import", headingStyle);
            GUILayout.Space(8);
            GUILayout.Label("Demo File", smallStyle);
            GUILayout.BeginHorizontal();
            demoPath = GUILayout.TextField(demoPath, GUILayout.Height(28));
            if (GUILayout.Button("Browse...", GUILayout.Width(72), GUILayout.Height(28)))
            {
                var selected = NativeDemoFilePicker.Pick();
                if (!string.IsNullOrEmpty(selected))
                    demoPath = selected;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("Target player (leave empty to select the top fragger)", smallStyle);
            targetPlayer = GUILayout.TextField(targetPlayer, GUILayout.Height(28));
            GUILayout.Space(10);
            GUILayout.Label("Local Analysis Service", smallStyle);
            demoEndpoint = GUILayout.TextField(demoEndpoint, GUILayout.Height(28));
            GUILayout.Space(16);

            GUI.enabled = !demoRequestInFlight;
            if (GUILayout.Button(demoRequestInFlight ? "Analyzing..." : "Analyze Demo",
                    GUILayout.Height(38)))
                StartCoroutine(AnalyzeDemoFile());
            if (GUILayout.Button("Load Sample Report", GUILayout.Height(32)))
                StartCoroutine(LoadSampleDemo());
            GUI.enabled = true;

            GUILayout.Space(18);
            GUILayout.Label("MVP Analysis Metrics", headingStyle);
            GUILayout.Space(6);
            GUILayout.Label("- K / D / A and K/D", bodyStyle);
            GUILayout.Label("- Headshots and headshot percentage", bodyStyle);
            GUILayout.Label("- Total damage and ADR", bodyStyle);
            GUILayout.Label("- Opening kills and opening deaths", bodyStyle);
            GUILayout.Label("- Evidence-based training recommendations", bodyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Demos are sent to local FastAPI only and temporary files are removed.", smallStyle);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(rightRect.x + 18, rightRect.y + 16,
                rightRect.width - 36, rightRect.height - 32));
            if (demoResult == null)
            {
                var empty = new GUIStyle(headingStyle)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    "No analysis report yet\n\nStart the backend and load the sample report to verify the full pipeline.",
                    empty);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(demoResult.player.name, titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{demoResult.map_name} | {demoResult.rounds} rounds", smallStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label($"{demoResult.file_name} | Source: {demoResult.data_source}", smallStyle);
            GUILayout.Space(14);

            GUILayout.BeginHorizontal();
            DrawDemoMetric("Kills", demoResult.player.kills.ToString());
            DrawDemoMetric("Deaths", demoResult.player.deaths.ToString());
            DrawDemoMetric("K/D", demoResult.player.kd_ratio.ToString("0.00"));
            DrawDemoMetric("Headshots", demoResult.player.headshot_percentage.ToString("0.0") + "%");
            DrawDemoMetric("ADR", demoResult.player.adr.ToString("0.0"));
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.Label("Coach Insights", headingStyle);
            GUILayout.Space(8);
            if (demoResult.insights != null)
            {
                foreach (var insight in demoResult.insights)
                {
                    var severityColor = insight.severity == "warning"
                        ? new Color(1f, 0.72f, 0.24f)
                        : insight.severity == "good"
                            ? cyan
                            : new Color(0.55f, 0.72f, 1f);
                    var old = tipStyle.normal.textColor;
                    tipStyle.normal.textColor = severityColor;
                    GUILayout.Label(insight.title, tipStyle);
                    tipStyle.normal.textColor = old;
                    GUILayout.Label(insight.evidence, bodyStyle);
                    GUILayout.Label("Recommendation: " + insight.action, smallStyle);
                    GUILayout.Space(12);
                }
            }
            GUILayout.EndArea();
        }

        private void DrawDemoMetric(string label, string value)
        {
            GUILayout.BeginVertical(GUILayout.MinWidth(90));
            GUILayout.Label(value, scoreStyle, GUILayout.Height(38));
            var centered = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(label, centered);
            GUILayout.EndVertical();
        }

        private void DrawScore(string label, int value)
        {
            GUILayout.BeginVertical(GUILayout.MinWidth(80));
            GUILayout.Label(value.ToString(), scoreStyle, GUILayout.Height(38));
            var centered = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(label, centered);
            GUILayout.EndVertical();
        }
    }
}
