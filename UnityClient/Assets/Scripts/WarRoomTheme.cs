using System;
using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Single source of truth for the war-room scene: palette, geometry, typography, lighting and
    /// the placeholder content shown before a real report is loaded. The editor scene builder reads
    /// this asset instead of carrying hard-coded literals, so the layout can be retuned without
    /// touching build code.
    ///
    /// All colours are authored in sRGB (see <see cref="WarRoomColor"/>). All distances are metres.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS AI Coach/War Room Theme", fileName = "WarRoomTheme")]
    public sealed class WarRoomTheme : ScriptableObject
    {
        public const string AssetPath = "Assets/Art/Config/WarRoomTheme.asset";

        [SerializeField] private Palette palette = new Palette();
        [SerializeField] private Room room = new Room();
        [SerializeField] private TacticalScreen screen = new TacticalScreen();
        [SerializeField] private SideRail rail = new SideRail();
        [SerializeField] private HeaderBand header = new HeaderBand();
        [SerializeField] private ControlDeck deck = new ControlDeck();
        [SerializeField] private Capture capture = new Capture();
        [SerializeField] private Typography typography = new Typography();
        [SerializeField] private CanvasMetrics canvas = new CanvasMetrics();
        [SerializeField] private LightingRig lighting = new LightingRig();
        [SerializeField] private CameraRig cameraRig = new CameraRig();
        [SerializeField] private Content content = new Content();

        public Palette Colors => palette;
        public Room RoomMetrics => room;
        public TacticalScreen Screen => screen;
        public SideRail Rail => rail;
        public HeaderBand Header => header;
        public ControlDeck Deck => deck;
        public Capture Recording => capture;
        public Typography Text => typography;
        public CanvasMetrics Canvas => canvas;
        public LightingRig Lights => lighting;
        public CameraRig Camera => cameraRig;
        public Content Data => content;

        // ------------------------------------------------------------------ palette

        [Serializable]
        public sealed class Palette
        {
            [Header("Surfaces (near-black, cool)")]
            [ColorUsage(false)] public Color voidBackdrop = WarRoomColor.Hex("#05080C");
            [ColorUsage(false)] public Color floorBase = WarRoomColor.Hex("#0B1118");
            [ColorUsage(false)] public Color screenBase = WarRoomColor.Hex("#0A0F16");
            [ColorUsage(false)] public Color panelGlass = WarRoomColor.Hex("#0E141C");
            [ColorUsage(false)] public Color panelEdge = WarRoomColor.Hex("#1B2733");
            [ColorUsage(false)] public Color panelRaised = WarRoomColor.Hex("#131C26");

            [Header("Accents")]
            [ColorUsage(false)] public Color cyanPrimary = WarRoomColor.Hex("#00E5FF");
            [ColorUsage(false)] public Color cyanDim = WarRoomColor.Hex("#00C2D9");
            [ColorUsage(false)] public Color blueElectric = WarRoomColor.Hex("#3B82F6");
            [ColorUsage(false)] public Color amberAlert = WarRoomColor.Hex("#FF6B2C");

            [Header("Text")]
            [ColorUsage(false)] public Color textPrimary = WarRoomColor.Hex("#FFFFFF");
            [ColorUsage(false)] public Color textBright = WarRoomColor.Hex("#C8D6E0");
            [ColorUsage(false)] public Color textSecondary = WarRoomColor.Hex("#8FA3B3");
            [ColorUsage(false)] public Color textMuted = WarRoomColor.Hex("#5A6B7A");

            [Header("Intensity multipliers")]
            [Tooltip("Emission strength for hairlines rendered with Standard rather than Unlit.")]
            public float glowIntensity = 1f;

            [Tooltip("How far floor guides are dimmed so they never compete with the main screen.")]
            [Range(0.05f, 1f)] public float floorGuideDim = 0.42f;

            [Tooltip("Dim factor for the thin frame that outlines panels and the main screen.")]
            [Range(0.05f, 1f)] public float frameDim = 0.55f;

            [Tooltip("Opacity of glass panel fills drawn on canvases.")]
            [Range(0f, 1f)] public float glassOpacity = 0.90f;
        }

        // ------------------------------------------------------------------ room

        [Serializable]
        public sealed class Room
        {
            [Header("Shell")]
            public Vector3 floorCenter = new Vector3(0f, -0.45f, 2.5f);
            public Vector3 floorSize = new Vector3(20f, 0.5f, 15f);
            public Vector3 backWallCenter = new Vector3(0f, 4.5f, 8.6f);
            public Vector3 backWallSize = new Vector3(20f, 10f, 0.4f);
            public float sideWallX = 9.5f;
            public Vector3 sideWallCenter = new Vector3(0f, 3.6f, 2.5f);
            public Vector3 sideWallSize = new Vector3(0.3f, 8f, 13f);

            [Header("Floor guides (sparse, faint, and sitting on the platform surface)")]
            [Tooltip("Guides ride just above the platform top; the raw floor is hidden behind it.")]
            public int railCount = 5;
            public float railSpacing = 2.7f;
            public float railLength = 7.4f;
            public float railThickness = 0.02f;
            public float railY = 0.036f;
            public float railZ = 3.4f;

            public int tickCount = 2;
            public float tickZStart = 0f;
            public float tickSpacing = 4.8f;
            public float tickLength = 14.4f;
            public float tickThickness = 0.02f;
            public float tickY = 0.032f;

            [Header("Stage")]
            public Vector3 platformCenter = new Vector3(0f, -0.06f, 3.4f);
            public Vector3 platformSize = new Vector3(15f, 0.18f, 7.6f);
            public float platformEdgeThickness = 0.022f;

            [Header("Frame")]
            public float columnX = 7.55f;
            public float columnY = 3.5f;
            public float columnZ = 6.6f;
            public Vector3 columnSize = new Vector3(0.16f, 7f, 0.3f);
            public float columnAccentThickness = 0.032f;
            public float columnAccentInset = 0.17f;

            public float beamY = 7.05f;
            public float beamZ = 6.6f;
            public Vector3 beamSize = new Vector3(15.4f, 0.14f, 0.3f);
            public float beamHairlineDrop = 0.1f;
            public float beamHairlineLength = 15f;
        }

        // ------------------------------------------------------------------ tactical screen

        [Serializable]
        public sealed class TacticalScreen
        {
            [Header("Anchor and hero surface")]
            public Vector3 anchor = new Vector3(0f, 3.85f, 6.3f);

            [Tooltip("Width of the display surface. Height is derived from Aspect so the video never stretches.")]
            public float width = 7.6f;

            [Tooltip("16:9 keeps the surface aligned with the 1920x1080 render texture and the AI overlay.")]
            public float aspect = 16f / 9f;

            public float surfaceDepth = 0.08f;

            [Header("Bezel")]
            public float backplatePadding = 0.32f;
            public float backplateDepth = 0.16f;
            public float backplateZ = 0.1f;

            [Header("Frame hairlines and corner brackets")]
            public float frameZ = -0.06f;
            public float frameMargin = 0.0625f;
            public float frameThickness = 0.022f;
            public float bracketLength = 0.85f;
            public float bracketThickness = 0.032f;

            [Header("Scanning reticle")]
            public float reticleZ = -0.16f;
            public float reticleArmLength = 0.62f;
            public float reticleThickness = 0.028f;
            public float reticleRotationSpeed = 22f;

            [Header("Replay marker pool")]
            public int markerCount = 10;
            public float markerDiameter = 0.1f;
            public float markerZ = -0.16f;

            [Header("Status strip above the surface")]
            public float statusStripHeight = 0.3f;
            public float statusStripGap = 0.14f;
            public float statusStripDepth = 0.1f;

            [Header("Timeline below the surface")]
            public float timelineGap = 0.48f;
            public float trackWidth = 7.3f;
            public float trackHeight = 0.1f;
            public float trackDepth = 0.08f;
            public float progressHeight = 0.06f;
            public float progressMinWidth = 0.04f;
            public int eventCount = 6;
            public float eventSpacing = 1.2f;
            public float eventDiameter = 0.1f;

            [Tooltip("Only this marker is allowed to use the amber alert colour.")]
            public int highPriorityEventIndex = 3;

            public float Height => width / Mathf.Max(0.1f, aspect);
            public float HalfWidth => width * 0.5f;
            public float HalfHeight => Height * 0.5f;
            public Vector2 BackplateSize => new Vector2(
                width + backplatePadding,
                Height + backplatePadding);

            public float FrameHalfWidth => HalfWidth + frameMargin;
            public float FrameHalfHeight => HalfHeight + frameMargin;

            /// <summary>Local Y of the status strip centre, just above the frame.</summary>
            public float StatusStripY => FrameHalfHeight + statusStripGap + statusStripHeight * 0.5f;

            /// <summary>Local Y of the timeline group.</summary>
            public float TimelineY => -(FrameHalfHeight + timelineGap);
        }

        // ------------------------------------------------------------------ side rails

        [Serializable]
        public sealed class SideRail
        {
            [Tooltip("Kept inside the 30 degree frustum so the rails never clip at the frame edge.")]
            public float offsetX = 5.45f;
            public float centerY = 3.85f;
            public float centerZ = 6.42f;

            public Vector2 backplateSize = new Vector2(2.6f, 4.6f);
            public float backplateDepth = 0.14f;
            public float backplateZ = 0.1f;

            public float hairlineZ = -0.05f;
            public float hairlineThickness = 0.022f;

            [Tooltip("Canvas is inset from the backplate so the 3D edge stays visible around the 2D content.")]
            public Vector2 canvasInset = new Vector2(0.2f, 0.24f);
            public float canvasZ = -0.09f;

            [Header("Match library rows")]
            public float rowHeight = 0.72f;
            public float rowGap = 0.06f;
            public float rowIndicatorWidth = 0.05f;
            public float headerHeight = 0.42f;

            [Header("Library footer (demo import actions)")]
            [Tooltip("Two side-by-side action buttons below the row stack.")]
            public float footerButtonHeight = 0.4f;

            [Tooltip("Share of the content width taken by the primary IMPORT DEMO button.")]
            [Range(0.5f, 0.85f)] public float footerPrimaryShare = 0.66f;

            public float footerButtonGap = 0.06f;
            public float footerStatusHeight = 0.26f;
            public float footerTopGap = 0.08f;

            [Header("Insight metrics")]
            [Tooltip("Tightened from 0.62 so three insight cards fit under three metric bars.")]
            public float metricRowHeight = 0.54f;
            public float metricBarHeight = 0.075f;
            [Tooltip("Kept equal to the canvas content width so bars align flush with the labels.")]
            public float metricBarWidth = 2.26f;
            public float cardHeight = 0.62f;
            public float cardGap = 0.08f;
            public float cardIndicatorWidth = 0.055f;

            public Vector2 CanvasSize => new Vector2(
                backplateSize.x - canvasInset.x,
                backplateSize.y - canvasInset.y);
        }

        // ------------------------------------------------------------------ header

        [Serializable]
        public sealed class HeaderBand
        {
            public float centerY = 6.72f;
            public float centerZ = 6.44f;

            [Tooltip("Narrower than the visible frustum width so the mode chip never clips.")]
            public Vector2 canvasSize = new Vector2(13.4f, 0.62f);

            [Tooltip("Sits just left of the brand text as a system-live indicator.")]
            public Vector3 beaconPosition = new Vector3(-6.55f, 6.72f, 6.38f);
            public float beaconDiameter = 0.13f;
            public float beaconPulseSpeed = 2f;
            public float beaconPulseAmount = 0.09f;
        }

        // ------------------------------------------------------------------ control deck

        [Serializable]
        public sealed class ControlDeck
        {
            public Vector3 deckCenter = new Vector3(0f, 0.86f, 3.05f);
            public Vector3 deckSize = new Vector3(9.2f, 0.26f, 2f);
            public float deckTiltX = -7f;
            public float deckEdgeThickness = 0.022f;

            public float buttonRowY = 1.24f;
            public float buttonRowZ = 2.42f;

            /// <summary>
            /// Narrowed from 2.3 when the deck grew from three buttons to five. Five buttons of the
            /// old width span 13 m, well past both the deck slab and the camera frustum.
            /// </summary>
            public Vector2 buttonSize = new Vector2(1.56f, 0.5f);

            [Tooltip("Gap between buttons that belong to the same group.")]
            public float buttonGap = 0.18f;

            [Tooltip("Wider gap separating the playback group from the capture group.")]
            public float buttonGroupGap = 0.46f;

            public Vector2 canvasSize = new Vector2(9.4f, 0.86f);

            [Tooltip("Border thickness of ghost buttons, in metres.")]
            public float buttonBorder = 0.018f;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Video capture of the war-room camera, encoded to H.264 by Unity's bundled encoder. Read by
        /// <see cref="ClipRecorder"/>.
        /// </summary>
        [Serializable]
        public sealed class Capture
        {
            [Tooltip("Arm the rolling buffer as soon as the scene starts, so a clip is always savable.")]
            public bool bufferOnStart = true;

            /// <summary>
            /// The rolling buffer is two segments staggered by this interval, so SAVE CLIP returns at
            /// least this much history and at most twice it.
            /// </summary>
            [Tooltip("Shortest tail SAVE CLIP is guaranteed to cover, in seconds.")]
            public int clipSeconds = 30;

            /// <summary>
            /// The whole war-room view is encoded, and the tactical screen occupies roughly 60% of its
            /// width, so 1080p leaves about 1150 px of actual game footage. Dropping to 720p leaves
            /// about 770 px, which starts to cost detail when reviewing crosshair placement.
            /// </summary>
            public Vector2Int resolution = new Vector2Int(1920, 1080);

            public int frameRate = 30;

            /// <summary>
            /// Empty means <c>persistentDataPath/Clips</c>. A relative path resolves against the Unity
            /// project folder, so the default climbs one level to sit beside it in the repository root
            /// rather than burying clips under AppData where they are awkward to find.
            ///
            /// It stays outside <c>Assets</c> deliberately: Unity would otherwise import every clip as a
            /// project asset. Point this at the backend's FPS_VISION_MEDIA_ROOT instead when clips should
            /// be analyzable by the vision service without being copied first.
            /// </summary>
            [Tooltip("Absolute or project-relative output folder. Empty uses persistentDataPath/Clips.")]
            public string outputDirectory = "../genVedio";
        }

        // ------------------------------------------------------------------ typography

        [Serializable]
        public sealed class Typography
        {
            [Header("Em height in metres (cap height is roughly 70% of this)")]
            public float headerTitle = 0.3f;
            public float headerMeta = 0.15f;
            public float sectionLabel = 0.16f;
            public float rowPrimary = 0.18f;
            public float rowSecondary = 0.12f;
            public float metricLabel = 0.145f;
            public float metricValue = 0.32f;
            public float cardTitle = 0.155f;
            public float cardBody = 0.125f;

            [Header("Auto-size floors for service-supplied copy")]
            [Tooltip("Insight titles arrive as full sentences, so they shrink toward this to stay in the card.")]
            public float cardTitleFloor = 0.095f;

            public float cardBodyFloor = 0.088f;
            public float buttonLabel = 0.19f;

            [Tooltip("Rail footer actions are narrower than the deck, so their labels step down a size.")]
            public float footerButtonLabel = 0.12f;

            public float screenStatus = 0.16f;
            public float timecode = 0.16f;

            [Header("Tracking (TMP character spacing)")]
            public float trackingTitle = 12f;
            public float trackingLabel = 8f;
            public float trackingBody = 0f;

            [Header("Outline keeps thin text readable against the video surface")]
            [Range(0f, 0.5f)] public float outlineWidth = 0.1f;
            public Color outlineColor = new Color(0f, 0.02f, 0.05f, 0.85f);
        }

        // ------------------------------------------------------------------ canvas

        [Serializable]
        public sealed class CanvasMetrics
        {
            [Tooltip("Canvas units per world metre. 1000 means a 1 m panel authors as 1000 px.")]
            public float unitsPerMeter = 1000f;

            [Tooltip("Higher values sharpen world-space text at the cost of a larger glyph atlas.")]
            public float dynamicPixelsPerUnit = 12f;

            public int sortingOrderHeader = 20;
            public int sortingOrderRail = 20;
            public int sortingOrderScreen = 30;
            public int sortingOrderDeck = 40;
            public int sortingOrderScreenSpace = 100;

            public float Scale => 1f / Mathf.Max(1f, unitsPerMeter);

            /// <summary>Converts a world size in metres to canvas units.</summary>
            public Vector2 ToUnits(Vector2 meters) => meters * unitsPerMeter;

            public float ToUnits(float meters) => meters * unitsPerMeter;
        }

        // ------------------------------------------------------------------ lighting

        [Serializable]
        public sealed class LightingRig
        {
            [Header("Directional key (the only shadow caster)")]
            public Vector3 keyRotation = new Vector3(42f, -22f, 0f);

            [Tooltip("Light colours are authored as final render values, not sRGB swatches.")]
            public Color keyColor = new Color(0.42f, 0.55f, 0.8f, 1f);

            [Tooltip("Deliberately low: panels read through their hairlines and text, not through lit surfaces.")]
            public float keyIntensity = 0.55f;

            [Header("Cyan accent point")]
            public Vector3 accentPosition = new Vector3(-4.4f, 4.6f, 3.2f);
            public Color accentColor = new Color(0.05f, 0.72f, 0.95f, 1f);

            [Tooltip("A localised pool, not a room wash. Anything above ~2 floods the whole set.")]
            public float accentIntensity = 1.5f;
            public float accentRange = 8.5f;

            [Header("Blue fill point")]
            public Vector3 fillPosition = new Vector3(4.6f, 4.2f, 3f);
            public Color fillColor = new Color(0.1f, 0.24f, 0.92f, 1f);
            public float fillIntensity = 1.2f;
            public float fillRange = 8f;

            [Header("Environment")]
            [Tooltip("Flat ambient replaces the skybox so the room stays a controlled near-black void.")]
            public Color ambient = new Color(0.014f, 0.02f, 0.03f, 1f);
        }

        // ------------------------------------------------------------------ camera

        [Serializable]
        public sealed class CameraRig
        {
            public Vector3 position = new Vector3(0f, 2.92f, -8.4f);
            public Vector3 lookAt = new Vector3(0f, 3.55f, 5f);

            [Range(24f, 36f)] public float fieldOfView = 30f;
            public float nearClip = 0.1f;
            public float farClip = 60f;
            public Color background = new Color(0.004f, 0.007f, 0.011f, 1f);
        }

        // ------------------------------------------------------------------ content

        [Serializable]
        public sealed class Content
        {
            public string productName = "FPS COACH";
            public string moduleName = "ANALYSIS STUDIO";
            public string buildTag = "WAR ROOM  v0.2";

            public string libraryTitle = "MATCH LIBRARY";
            public string insightsTitle = "COACH INSIGHTS";
            public string screenTitle = "VIDEO REVIEW";

            /// <summary>
            /// Four rows, not five: the library rail also carries the import footer, and five rows
            /// filled the canvas edge to edge. Row 0 is overwritten by each analyzed report, so it is
            /// authored as the most recent match.
            /// </summary>
            public MatchEntry[] matches =
            {
                new MatchEntry { map = "MIRAGE", score = "13 : 9", meta = "TODAY  ·  34 MIN" },
                new MatchEntry { map = "INFERNO", score = "10 : 13", meta = "TODAY  ·  41 MIN" },
                new MatchEntry { map = "ANUBIS", score = "13 : 11", meta = "YESTERDAY  ·  38 MIN" },
                new MatchEntry { map = "NUKE", score = "7 : 13", meta = "YESTERDAY  ·  29 MIN" }
            };

            /// <summary>
            /// Labels match the three headline numbers a demo report actually carries, so the rail does
            /// not relabel itself the moment a real report replaces the placeholder values.
            ///
            /// The pre-report state is authored as empty rather than as plausible-looking figures: a
            /// filled bar reading "55" under a "K / D" label would be indistinguishable from analysis
            /// output that nobody produced.
            /// </summary>
            public MetricEntry[] metrics =
            {
                new MetricEntry { label = "K / D", value = 0f, display = "--" },
                new MetricEntry { label = "HS %", value = 0f, display = "--" },
                new MetricEntry { label = "ADR", value = 0f, display = "--" }
            };

            /// <summary>Three cards, because the analysis service returns exactly three insights.</summary>
            public InsightEntry[] insights =
            {
                new InsightEntry
                {
                    title = "OPENING DUELS",
                    body = "Review trade spacing on A entry",
                    highPriority = true
                },
                new InsightEntry
                {
                    title = "FIRST SHOT",
                    body = "Hold head level before the peek",
                    highPriority = false
                },
                new InsightEntry
                {
                    title = "ROUND IMPACT",
                    body = "Convert damage into trades",
                    highPriority = false
                }
            };

            public string buttonImport = "IMPORT VIDEO";
            public string buttonPlay = "PLAY";
            public string buttonPause = "PAUSE";
            public string buttonLive = "LIVE MODE";
            public string buttonDemo = "DEMO MODE";

            [Header("Capture (war-room camera to MP4)")]
            public string buttonRecord = "RECORD";

            [Tooltip("Same button as buttonRecord, shown while a take is running.")]
            public string buttonRecordStop = "STOP REC";

            public string buttonSaveClip = "SAVE CLIP";

            public string captureStatusIdle = "CAPTURE OFF";
            public string captureStatusBuffering = "BUFFERING LAST {0}s  ·  SAVE CLIP TO KEEP";
            public string captureStatusRecording = "RECORDING  ·  {0}";
            public string captureStatusSaving = "WRITING VIDEO";
            public string captureStatusSaved = "SAVED  ·  {0}";
            public string captureStatusFailed = "CAPTURE FAILED  ·  {0}";
            public string captureStatusUnavailable = "CAPTURE UNAVAILABLE  ·  {0}";

            [Tooltip("Shown when a file closed empty, so the recorder never reports an unplayable clip as saved.")]
            public string captureStatusNoFrames = "NO FRAMES CAPTURED";

            [Header("Demo analysis (CS2 .dem via the local service)")]
            public string buttonImportDemo = "IMPORT DEMO";
            public string buttonSampleReport = "SAMPLE";

            public string demoEndpoint = "http://127.0.0.1:8000/api/v1/analyze/demo";

            [Tooltip("Empty means the service picks the top fragger.")]
            public string demoTargetPlayer = "";

            /// <summary>
            /// The request body is one buffer holding the whole demo, so the ceiling is available
            /// memory rather than the service limit. Raised past the original 512 because the body is
            /// now filled by a single streamed pass instead of a read plus a multipart copy; this
            /// covers the largest reference demo in the project at roughly 650 MB. The service itself
            /// accepts up to 1.5 GB.
            /// </summary>
            [Tooltip("Client-side upload cap in MB. The service accepts up to 1500.")]
            public int demoMaxUploadMegabytes = 1024;

            [Tooltip("A 430 MB demo parses in roughly 15 s locally; this leaves room for larger files.")]
            public int demoTimeoutSeconds = 180;

            public int demoSampleTimeoutSeconds = 10;

            [Header("Metric bar ceilings (a full bar means 'excellent')")]
            [Tooltip("K/D that fills the bar completely.")]
            public float metricKdCeiling = 2f;

            [Tooltip("ADR that fills the bar completely.")]
            public float metricAdrCeiling = 120f;

            [Header("Demo status line")]
            public string demoStatusIdle = "NO REPORT  ·  IMPORT A DEMO";
            public string demoStatusSelecting = "SELECTING DEMO";
            public string demoStatusMissing = "FILE NOT FOUND";
            public string demoStatusWrongType = "ONLY .dem FILES ARE SUPPORTED";
            public string demoStatusTooLarge = "DEMO IS {0:N0} MB  ·  LIMIT IS {1} MB";
            public string demoStatusReading = "READING {0}";
            public string demoStatusAnalyzing = "ANALYZING {0}";
            public string demoStatusSample = "LOADING SAMPLE REPORT";
            public string demoStatusReady = "{0}  ·  {1} ROUNDS";
            public string demoStatusInvalid = "SERVICE RETURNED AN INVALID REPORT";
            public string demoStatusOffline = "SERVICE UNREACHABLE  ·  START Backend/run.ps1";
            public string demoStatusTooLargeForBuffer = "DEMO EXCEEDS THE 2 GB REQUEST BUFFER";
            public string demoStatusOutOfMemory = "NOT ENOUGH MEMORY TO BUFFER THIS DEMO";
            public string demoStatusReadTruncated = "DEMO READ ENDED EARLY";

            public string statusNoVideo = "NO SIGNAL";
            public string statusSelecting = "SELECTING SOURCE";
            public string statusLoading = "LOADING";
            public string statusReadyPrefix = "READY";
            public string statusError = "DECODE ERROR";
            public string statusUnsupported = "UNSUPPORTED FORMAT";
            public string statusMissing = "FILE NOT FOUND";
            public string statusLive = "LIVE CAPTURE";
            public string statusLiveUnavailable = "NO CAPTURE DEVICE";
            public string modeDemo = "DEMO";
            public string modeLive = "LIVE";
        }

        [Serializable]
        public struct MatchEntry
        {
            public string map;
            public string score;
            public string meta;
        }

        [Serializable]
        public struct MetricEntry
        {
            public string label;
            [Range(0f, 1f)] public float value;

            [Tooltip("Readout text. Leave empty to print the bar fill as a percentage.")]
            public string display;
        }

        [Serializable]
        public struct InsightEntry
        {
            public string title;
            public string body;
            public bool highPriority;
        }
    }
}
