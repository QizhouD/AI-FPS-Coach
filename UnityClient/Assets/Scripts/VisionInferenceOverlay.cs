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
        private const float OverlayHeight = 405f;

        [Header("Vision API")]
        [SerializeField] private string frameEndpoint =
            "http://127.0.0.1:8000/api/v1/vision/frame";
        [SerializeField] private float sampleRate = 5f;
        [SerializeField] private int jpegQuality = 70;
        [SerializeField] private int captureWidth = 960;
        [SerializeField] private bool preferVideoPathJobs = true;

        [Header("Overlay")]
        [SerializeField] private Color enemyBoxColor = new Color(1f, 0.24f, 0.12f, 0.72f);
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
        private readonly List<RawImage> enemyBoxes = new List<RawImage>();
        private LineRenderer aimLine;
        private Texture2D whiteTexture;
        private Material lineMaterial;
        private float nextSampleTime;
        private int frameIndex;
        private bool requestInFlight;
        private bool hasResponse;
        private bool videoJobActive;
        private string videoJobId;
        private readonly List<VisionFrameResponse> videoResults =
            new List<VisionFrameResponse>();

        private void Start()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            viewportTransform = FindDeepChild(transform, "Tactical Viewport");
            if (videoPlayer == null || viewportTransform == null)
                return;

            sourceTexture = videoPlayer.targetTexture;
            CreateOverlay();
            nextSampleTime = 0f;

            var view = GetComponent<CoachStudioTemplateView>();
            if (view != null)
                view.VideoPathLoaded += HandleVideoPathLoaded;
        }

        private void OnDestroy()
        {
            var view = GetComponent<CoachStudioTemplateView>();
            if (view != null)
                view.VideoPathLoaded -= HandleVideoPathLoaded;
            if (whiteTexture != null)
                Destroy(whiteTexture);
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
                sample_rate = sampleRate
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

        private IEnumerator PollVideoJob()
        {
            while (videoJobActive && !string.IsNullOrEmpty(videoJobId))
            {
                using (var request = UnityWebRequest.Get(
                    frameEndpoint.Replace(
                        "/frame",
                        "/jobs/" + videoJobId)))
                {
                    request.timeout = 8;
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
                    if (response.status == "completed")
                    {
                        videoResults.Clear();
                        if (response.results != null)
                            videoResults.AddRange(response.results);
                        videoJobActive = false;
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

        private void ApplyVideoJobResult()
        {
            if (videoResults.Count == 0 || videoPlayer == null)
                return;

            VisionFrameResponse latest = null;
            for (var index = 0; index < videoResults.Count; index++)
            {
                if (videoResults[index].timestamp <= videoPlayer.time)
                    latest = videoResults[index];
                else
                    break;
            }
            if (latest != null)
                ApplyResponse(latest);
        }

        private void CreateOverlay()
        {
            var canvasObject = new GameObject(
                "Vision Overlay Canvas",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.WorldSpace;
            overlayCanvas.worldCamera = Camera.main;
            overlayCanvas.sortingOrder = 120;
            canvasObject.transform.localPosition =
                viewportTransform.localPosition + new Vector3(0f, 0f, -0.065f);
            canvasObject.transform.localRotation = viewportTransform.localRotation;

            overlayRect = canvasObject.GetComponent<RectTransform>();
            overlayRect.sizeDelta = new Vector2(OverlayWidth, OverlayHeight);
            overlayRect.localScale = Vector3.one *
                (viewportTransform.localScale.x / OverlayWidth);

            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();

            crosshairMarker = CreateMarker(
                "Actual Crosshair",
                new Vector2(12f, 12f),
                crosshairColor);
            recommendedMarker = CreateMarker(
                "Recommended Aim",
                new Vector2(20f, 20f),
                recommendedColor);
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

        private RawImage CreateMarker(string name, Vector2 size, Color color)
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
            image.texture = whiteTexture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private RawImage CreateEnemyBox(int index)
        {
            return CreateMarker(
                "Enemy Box " + index,
                Vector2.zero,
                enemyBoxColor);
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
                var offsetX = hasTarget ? recommended.offset_x : 0f;
                var offsetY = hasTarget ? recommended.offset_y : 0f;
                var targetCount = response.enemies == null
                    ? 0
                    : response.enemies.Length;
                metricsLabel.text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "AI VISION  {0:0.0} ms\n" +
                    "CROSSHAIR  {1}  {2:P0}\n" +
                    "AIM POINT  {3:P0}  OFFSET X {4:+0.000;-0.000;0.000} Y {5:+0.000;-0.000;0.000}\n" +
                    "HEAD TARGETS  {6}",
                    response.inference_ms,
                    actualStatus,
                    actualConfidence,
                    recommendedConfidence,
                    offsetX,
                    offsetY,
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
                rect.sizeDelta = new Vector2(
                    Mathf.Max(
                        3f,
                        (enemy.x2 - enemy.x1) * OverlayWidth),
                    Mathf.Max(
                        3f,
                        (enemy.y2 - enemy.y1) * OverlayHeight));
                SetMarkerPosition(
                    box,
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
            marker.rectTransform.anchoredPosition = new Vector2(
                (Mathf.Clamp01(normalizedX) - 0.5f) * OverlayWidth,
                (0.5f - Mathf.Clamp01(normalizedY)) * OverlayHeight);
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
