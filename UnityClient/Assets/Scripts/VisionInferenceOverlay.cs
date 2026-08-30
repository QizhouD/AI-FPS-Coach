using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

namespace FpsAiCoach
{
    public sealed class VisionInferenceOverlay : MonoBehaviour
    {
        private const float OverlayWidth = 1000f;

        /// <summary>
        /// Derived from the display surface at runtime rather than hard-coded, so detection boxes stay
        /// registered with the video no matter how the screen is proportioned.
        /// </summary>
        private float overlayHeight = OverlayWidth * 9f / 16f;

        [Header("Vision API")]
        [SerializeField] private string frameEndpoint =
            "http://127.0.0.1:8000/api/v1/vision/frame";
        [Tooltip("Shot alignment fails below 8 fps: the backend tolerance is 0.12 s, " +
                 "so most shots land between samples and go unaligned.")]
        [SerializeField] private float sampleRate = 10f;
        [SerializeField] private int jpegQuality = 70;
        [SerializeField] private int captureWidth = 960;
        [SerializeField] private bool preferVideoPathJobs = true;

        [Header("Analysis")]
        [Tooltip("The in-game FOV the footage was recorded at, horizontal at 4:3. " +
                 "Deviations cannot be expressed in degrees without it.")]
        [SerializeField] private float fovDegrees = 90f;
        [Tooltip("Deviation below which the crosshair counts as being on target.")]
        [SerializeField] private float trackingThresholdDegrees = 5f;
        [SerializeField] private bool detectShots = true;

        [Header("Overlay")]
        [Tooltip("Outline for a body box.")]
        [SerializeField] private Color enemyBoxColor = new Color(1f, 0.3f, 0.16f, 0.95f);
        [Tooltip("Outline for a head box, so a detected head reads differently from a body " +
                 "whose head the service had to infer.")]
        [SerializeField] private Color enemyHeadBoxColor = new Color(0.35f, 1f, 0.55f, 0.95f);
        [Tooltip("Thickness of a detection outline, in the overlay's units. The overlay is " +
                 "1000 units wide across the whole frame, so this is roughly pixels.")]
        [SerializeField, Range(1f, 6f)] private float boxOutlineWidth = 2f;
        [SerializeField] private Color crosshairColor = new Color(0.15f, 1f, 1f, 0.95f);
        [SerializeField] private Color recommendedColor = new Color(1f, 0.75f, 0.08f, 0.98f);

        private VideoPlayer videoPlayer;
        private RenderTexture sourceTexture;
        private Transform viewportTransform;
        private Canvas overlayCanvas;
        private RectTransform overlayRect;
        private RawImage crosshairMarker;
        private RawImage recommendedMarker;
        private TextMeshProUGUI metricsLabel;
        private readonly List<Image> enemyBoxes = new List<Image>();
        private LineRenderer aimLine;
        private Texture2D whiteTexture;
        private Texture2D ringTexture;
        private Sprite outlineSprite;
        private Material lineMaterial;
        private float nextSampleTime;
        private int frameIndex;
        private bool requestInFlight;
        private bool hasResponse;
        private bool videoJobActive;
        private string videoJobId;
        private readonly List<VisionFrameResponse> videoResults =
            new List<VisionFrameResponse>();
        private int resultCursor;
        private int appliedIndex = -1;
        private double lastPlaybackTime;

        /// <summary>
        /// Raised once a video job finishes with session-level aim metrics attached.
        /// </summary>
        public event Action<VisionSessionMetrics> MetricsReady;

        public VisionSessionMetrics LatestMetrics { get; private set; }

        /// <summary>
        /// Read-only view of the analysed frames, exposed so a verification pass can check that
        /// the detection being drawn is the one belonging to the current playback position.
        /// Seeking correctness is otherwise unobservable from outside: the overlay would look
        /// plausible while showing a frame from the wrong moment.
        /// </summary>
        public IReadOnlyList<VisionFrameResponse> Results => videoResults;

        /// <summary>Index into <see cref="Results"/> of the detection currently drawn, or -1.</summary>
        public int AppliedIndex => appliedIndex;

        /// <summary>True while a video job is still being submitted or polled.</summary>
        public bool JobActive => videoJobActive;

        private void Start()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            viewportTransform = FindDeepChild(transform, "Tactical Viewport");
            if (videoPlayer == null || viewportTransform == null)
                return;

            sourceTexture = videoPlayer.targetTexture;
            CreateOverlay();
            nextSampleTime = 0f;
        }

        /// <summary>
        /// Only the subscription moves to enable, so that it survives the domain reload a script edit during
        /// play causes. Building the overlay stays in Start deliberately: it spawns objects, so running it
        /// per enable would stack a second set on top of the first.
        /// </summary>
        private void OnEnable()
        {
            var screen = GetComponent<TacticalScreenController>();
            if (screen != null)
                screen.VideoPathLoaded += HandleVideoPathLoaded;
        }

        private void OnDisable()
        {
            var screen = GetComponent<TacticalScreenController>();
            if (screen != null)
                screen.VideoPathLoaded -= HandleVideoPathLoaded;
        }

        private void OnDestroy()
        {
            if (whiteTexture != null)
                Destroy(whiteTexture);
            if (ringTexture != null)
                Destroy(ringTexture);
            if (outlineSprite != null)
            {
                Destroy(outlineSprite.texture);
                Destroy(outlineSprite);
            }
            if (lineMaterial != null)
                Destroy(lineMaterial);
        }

        private void Update()
        {
            ApplyVideoJobResult();
            if (videoJobActive || videoResults.Count > 0)
                return;

            if (
                videoPlayer == null ||
                sourceTexture == null ||
                !videoPlayer.isPrepared ||
                requestInFlight)
            {
                return;
            }

            if (videoPlayer.time + 0.0001d >= nextSampleTime)
            {
                nextSampleTime = (float)videoPlayer.time + 1f / Mathf.Max(1f, sampleRate);
                StartCoroutine(CaptureAndSendFrame((float)videoPlayer.time, frameIndex++));
            }
        }

        private void HandleVideoPathLoaded(string path)
        {
            videoResults.Clear();
            resultCursor = 0;
            appliedIndex = -1;
            lastPlaybackTime = 0d;
            LatestMetrics = null;
            videoJobId = null;
            videoJobActive = false;
            if (preferVideoPathJobs)
                StartCoroutine(SubmitVideoJob(path));
        }

        private IEnumerator SubmitVideoJob(string path)
        {
            videoJobActive = true;
            var payload = JsonUtility.ToJson(new VisionVideoJobRequest
            {
                video_path = path,
                session_id = gameObject.scene.name,
                sample_rate = sampleRate,
                fov_deg = fovDegrees,
                tracking_threshold_deg = trackingThresholdDegrees,
                detect_shots = detectShots
            });
            using (var request = new UnityWebRequest(
                frameEndpoint.Replace("/frame", "/video"),
                UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(
                    System.Text.Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        "Video path job unavailable, using frame mode: " +
                        request.error);
                    videoJobActive = false;
                    yield break;
                }

                var response = JsonUtility.FromJson<VisionVideoJobResponse>(
                    request.downloadHandler.text);
                if (response == null || string.IsNullOrEmpty(response.job_id))
                {
                    videoJobActive = false;
                    yield break;
                }
                videoJobId = response.job_id;
            }

            yield return StartCoroutine(PollVideoJob());
        }

        /// <summary>
        /// Streams analysed frames while the job runs rather than waiting for it to finish.
        ///
        /// Each poll asks only for what has been added since the last one, so a long recording
        /// shows overlays from the start instead of nothing until the whole file is processed,
        /// and the same frames are not re-sent on every request.
        /// </summary>
        private IEnumerator PollVideoJob()
        {
            while (videoJobActive && !string.IsNullOrEmpty(videoJobId))
            {
                var endpoint =
                    frameEndpoint.Replace("/frame", "/jobs/" + videoJobId) +
                    "?results_from=" + videoResults.Count;

                using (var request = UnityWebRequest.Get(endpoint))
                {
                    request.timeout = 30;
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning(
                            "Video job polling failed, using frame mode: " +
                            request.error);
                        videoJobActive = false;
                        yield break;
                    }

                    var response = JsonUtility.FromJson<VisionVideoJobResponse>(
                        request.downloadHandler.text);
                    if (response == null)
                    {
                        videoJobActive = false;
                        yield break;
                    }

                    if (response.results != null && response.results.Length > 0)
                        videoResults.AddRange(response.results);

                    if (response.status == "completed")
                    {
                        videoJobActive = false;
                        if (response.metrics != null)
                        {
                            LatestMetrics = response.metrics;
                            MetricsReady?.Invoke(response.metrics);
                        }
                        yield break;
                    }
                    if (response.status == "failed")
                    {
                        Debug.LogWarning(
                            "Video job failed, using frame mode: " +
                            response.error);
                        videoJobActive = false;
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        /// <summary>
        /// Selects the detection for the current playback position using a forward cursor.
        ///
        /// Scanning from index zero every frame made the work grow with playback position, which
        /// a long round turns into thousands of comparisons a second. Playback is almost always
        /// monotonic, so the cursor advances in place and only a backward seek pays for a
        /// re-seek, which is done by bisection rather than another scan.
        /// </summary>
        private void ApplyVideoJobResult()
        {
            if (videoResults.Count == 0 || videoPlayer == null)
                return;

            var time = videoPlayer.time;
            if (time < lastPlaybackTime)
                resultCursor = FindIndexAt(time);
            lastPlaybackTime = time;

            while (resultCursor + 1 < videoResults.Count &&
                   videoResults[resultCursor + 1].timestamp <= time)
            {
                resultCursor++;
            }

            // Playback has not reached the first analysed frame yet.
            if (videoResults[resultCursor].timestamp > time)
                return;

            // Re-applying an unchanged frame would rebuild every marker and the metrics string
            // each tick, which is the bulk of the overlay's cost while paused.
            if (resultCursor == appliedIndex)
                return;

            appliedIndex = resultCursor;
            ApplyResponse(videoResults[resultCursor]);
        }

        private int FindIndexAt(double time)
        {
            var low = 0;
            var high = videoResults.Count - 1;
            var found = 0;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (videoResults[middle].timestamp <= time)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return found;
        }

        private void CreateOverlay()
        {
            var canvasObject = new GameObject(
                "Vision Overlay Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            // Parented alongside the viewport rather than to the root, so the overlay tracks the
            // surface wherever it sits in the hierarchy.
            canvasObject.transform.SetParent(viewportTransform.parent, false);
            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.WorldSpace;
            overlayCanvas.worldCamera = Camera.main;
            overlayCanvas.sortingOrder = 120;
            canvasObject.transform.localPosition =
                viewportTransform.localPosition + new Vector3(0f, 0f, -0.065f);
            canvasObject.transform.localRotation = viewportTransform.localRotation;

            var viewportScale = viewportTransform.localScale;
            overlayHeight = viewportScale.x > 0.0001f
                ? OverlayWidth * (viewportScale.y / viewportScale.x)
                : OverlayWidth * 9f / 16f;

            overlayRect = canvasObject.GetComponent<RectTransform>();
            overlayRect.sizeDelta = new Vector2(OverlayWidth, overlayHeight);
            overlayRect.localScale = Vector3.one * (viewportScale.x / OverlayWidth);

            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
            outlineSprite = CreateOutlineSprite();

            ringTexture = CreateRingTexture();

            crosshairMarker = CreateMarker(
                "Actual Crosshair",
                new Vector2(10f, 10f),
                crosshairColor);
            // A ring rather than a filled square: this marker points at the head the player
            // should be on, and a solid patch would sit on top of it.
            recommendedMarker = CreateMarker(
                "Recommended Aim",
                new Vector2(30f, 30f),
                recommendedColor,
                ringTexture);
            metricsLabel = CreateMetricsLabel();
            SetVisible(false);

            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            aimLine = gameObject.AddComponent<LineRenderer>();
            aimLine.material = lineMaterial;
            aimLine.startWidth = 0.012f;
            aimLine.endWidth = 0.012f;
            aimLine.positionCount = 2;
            aimLine.startColor = recommendedColor;
            aimLine.endColor = recommendedColor;
            aimLine.enabled = false;
            aimLine.sortingOrder = 121;
        }

        /// <summary>An anti-aliased ring, drawn once so the aim marker can frame a head.</summary>
        private static Texture2D CreateRingTexture()
        {
            const int Size = 64;
            const float Outer = 30f;
            const float Inner = 25f;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Vision Aim Ring",
                wrapMode = TextureWrapMode.Clamp,
            };

            var centre = (Size - 1) * 0.5f;
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = x - centre;
                    var dy = y - centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    // One pixel of falloff on each edge of the band, so the ring is not jagged.
                    var alpha = Mathf.Clamp01(Outer - distance) *
                                Mathf.Clamp01(distance - Inner + 1f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        private RawImage CreateMarker(string name, Vector2 size, Color color, Texture2D texture = null)
        {
            var markerObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(RawImage));
            markerObject.transform.SetParent(overlayRect, false);
            var rect = markerObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var image = markerObject.GetComponent<RawImage>();
            image.texture = texture != null ? texture : whiteTexture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A one-pixel border around a transparent centre, sliced so the border keeps its
        /// thickness however the box is stretched. A filled quad would hide the very target
        /// the box is pointing at, which is the whole reason for drawing it.
        /// </summary>
        private Sprite CreateOutlineSprite()
        {
            const int Size = 8;
            const int Border = 1;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Vision Box Outline",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Size * Size];
            var opaque = new Color32(255, 255, 255, 255);
            var clear = new Color32(255, 255, 255, 0);
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var onBorder = x < Border || y < Border ||
                                   x >= Size - Border || y >= Size - Border;
                    pixels[y * Size + x] = onBorder ? opaque : clear;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);

            // Image divides the border by sprite.pixelsPerUnit / canvas.referencePixelsPerUnit.
            // Matching the two makes that ratio one, so pixelsPerUnitMultiplier alone decides the
            // thickness. Leaving it at the default 1 blows a one-pixel border up to a hundred
            // units, which is wider than most boxes and renders as a solid block.
            var referencePixelsPerUnit = overlayCanvas != null
                ? overlayCanvas.referencePixelsPerUnit
                : 100f;

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                referencePixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                new Vector4(Border, Border, Border, Border));
        }

        private Image CreateEnemyBox(int index)
        {
            var boxObject = new GameObject(
                "Enemy Box " + index,
                typeof(RectTransform),
                typeof(Image));
            boxObject.transform.SetParent(overlayRect, false);

            var rect = boxObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = boxObject.GetComponent<Image>();
            image.sprite = outlineSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, boxOutlineWidth);
            image.fillCenter = false;
            image.color = enemyBoxColor;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI CreateMetricsLabel()
        {
            var labelObject = new GameObject(
                "Vision Metrics",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(overlayRect, false);
            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -12f);
            rect.sizeDelta = new Vector2(380f, 104f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.extraPadding = true;
            label.outlineWidth = 0.2f;
            label.outlineColor = Color.black;
            label.raycastTarget = false;
            label.text = "AI VISION\nWAITING FOR FRAME";
            return label;
        }

        private IEnumerator CaptureAndSendFrame(float timestamp, int currentFrameIndex)
        {
            requestInFlight = true;
            var previousActive = RenderTexture.active;
            var width = Mathf.Min(captureWidth, sourceTexture.width);
            var height = Mathf.RoundToInt(
                sourceTexture.height * (width / (float)sourceTexture.width));
            var captureTexture = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32);
            Graphics.Blit(sourceTexture, captureTexture);
            RenderTexture.active = captureTexture;
            var frameTexture = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
            frameTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            frameTexture.Apply(false);
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(captureTexture);
            var payload = frameTexture.EncodeToJPG(jpegQuality);
            Destroy(frameTexture);

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection(
                    "frame",
                    payload,
                    "frame.jpg",
                    "image/jpeg"),
                new MultipartFormDataSection(
                    "timestamp",
                    timestamp.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                new MultipartFormDataSection(
                    "frame_index",
                    currentFrameIndex.ToString()),
                new MultipartFormDataSection(
                    "session_id",
                    gameObject.scene.name)
            };

            using (var request = UnityWebRequest.Post(frameEndpoint, form))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<VisionFrameResponse>(
                        request.downloadHandler.text);
                    if (response != null)
                        ApplyResponse(response);
                }
                else if (request.responseCode != 503)
                {
                    Debug.LogWarning(
                        "Vision frame request failed: " + request.error);
                }
            }

            requestInFlight = false;
        }

        private void ApplyResponse(VisionFrameResponse response)
        {
            hasResponse = true;
            SetVisible(true);
            var actual = response.actual_crosshair;
            var recommended = response.recommended_aim;
            var hasTarget = recommended != null &&
                            !string.IsNullOrEmpty(recommended.target_id);
            if (metricsLabel != null)
            {
                var actualStatus = actual != null && actual.visible
                    ? (actual.source == "screen_center_baseline"
                        ? "CENTER BASELINE"
                        : "LOCKED")
                    : "NOT FOUND";
                var actualConfidence = actual != null
                    ? actual.confidence
                    : 0f;
                var recommendedConfidence = hasTarget
                    ? recommended.confidence
                    : 0f;
                var targetCount = response.enemies == null
                    ? 0
                    : response.enemies.Length;
                // Degrees rather than normalized pixels: a deviation in fractions of the frame
                // means nothing across resolutions, and it is the angle a player can act on.
                var deviation = hasTarget
                    ? string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0:0.0}\u00B0  X {1:+0.0;-0.0;0.0}\u00B0 Y {2:+0.0;-0.0;0.0}\u00B0",
                        recommended.offset_deg,
                        recommended.offset_deg_x,
                        recommended.offset_deg_y)
                    : "NO TARGET";
                metricsLabel.text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "AI VISION  {0:0.0} ms\n" +
                    "CROSSHAIR  {1}  {2:P0}\n" +
                    "DEVIATION  {3}\n" +
                    "AIM POINT  {4:P0}   DETECTIONS  {5}",
                    response.inference_ms,
                    actualStatus,
                    actualConfidence,
                    deviation,
                    recommendedConfidence,
                    targetCount);
            }
            if (response.actual_crosshair != null &&
                response.actual_crosshair.visible)
            {
                SetMarkerPosition(
                    crosshairMarker,
                    response.actual_crosshair.x,
                    response.actual_crosshair.y);
                crosshairMarker.color = crosshairColor;
                crosshairMarker.gameObject.SetActive(true);
            }
            else
            {
                crosshairMarker.gameObject.SetActive(false);
            }

            if (response.recommended_aim != null &&
                response.recommended_aim.target_id != null)
            {
                SetMarkerPosition(
                    recommendedMarker,
                    response.recommended_aim.x,
                    response.recommended_aim.y);
                recommendedMarker.gameObject.SetActive(true);
                if (crosshairMarker.gameObject.activeSelf)
                {
                    aimLine.enabled = true;
                    aimLine.SetPosition(
                        0,
                        MarkerWorldPosition(crosshairMarker));
                    aimLine.SetPosition(
                        1,
                        MarkerWorldPosition(recommendedMarker));
                }
            }
            else
            {
                recommendedMarker.gameObject.SetActive(false);
                aimLine.enabled = false;
            }

            var enemies = response.enemies ?? Array.Empty<VisionEnemy>();
            for (var index = 0; index < enemies.Length; index++)
            {
                while (enemyBoxes.Count <= index)
                    enemyBoxes.Add(CreateEnemyBox(enemyBoxes.Count));
                var box = enemyBoxes[index];
                var enemy = enemies[index];
                var rect = box.rectTransform;
                // A box narrower than the outline it is drawn with collapses into a blob, and a
                // head at range is only a few units across, so hold it at a legible minimum.
                var minimum = boxOutlineWidth * 3f;
                rect.sizeDelta = new Vector2(
                    Mathf.Max(
                        minimum,
                        (enemy.x2 - enemy.x1) * OverlayWidth),
                    Mathf.Max(
                        minimum,
                        (enemy.y2 - enemy.y1) * overlayHeight));
                box.color = enemy.part == "head" ? enemyHeadBoxColor : enemyBoxColor;
                SetBoxPosition(
                    rect,
                    (enemy.x1 + enemy.x2) * 0.5f,
                    (enemy.y1 + enemy.y2) * 0.5f);
                box.gameObject.SetActive(true);
            }
            for (var index = enemies.Length; index < enemyBoxes.Count; index++)
                enemyBoxes[index].gameObject.SetActive(false);
        }

        private void SetMarkerPosition(
            RawImage marker,
            float normalizedX,
            float normalizedY)
        {
            SetBoxPosition(marker.rectTransform, normalizedX, normalizedY);
        }

        private void SetBoxPosition(
            RectTransform rect,
            float normalizedX,
            float normalizedY)
        {
            rect.anchoredPosition = new Vector2(
                (Mathf.Clamp01(normalizedX) - 0.5f) * OverlayWidth,
                (0.5f - Mathf.Clamp01(normalizedY)) * overlayHeight);
        }

        private Vector3 MarkerWorldPosition(RawImage marker)
        {
            return marker.rectTransform.TransformPoint(Vector3.zero) +
                   overlayCanvas.transform.forward * -0.01f;
        }

        private void SetVisible(bool visible)
        {
            if (overlayCanvas != null)
                overlayCanvas.enabled = visible && hasResponse;
        }

        private static Transform FindDeepChild(
            Transform parent,
            string objectName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == objectName)
                    return child;
                var found = FindDeepChild(child, objectName);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
