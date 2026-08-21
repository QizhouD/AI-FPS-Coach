using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace FpsAiCoach
{
    public sealed class CoachStudioTemplateView : MonoBehaviour
    {
        private static readonly string[] SupportedVideoExtensions =
        {
            ".mp4",
            ".mov",
            ".webm",
            ".avi",
            ".m4v"
        };

        [Header("Scene Animation")]
        [SerializeField] private Transform scanningCore;
        [SerializeField] private Transform statusBeacon;
        [SerializeField] private float rotationSpeed = 24f;
        [SerializeField] private float pulseSpeed = 2.2f;
        [SerializeField] private float pulseAmount = 0.08f;

        [Header("Video Playback")]
        [SerializeField] private int videoTextureWidth = 1920;
        [SerializeField] private int videoTextureHeight = 1080;
        [SerializeField] private bool flipVideoHorizontally;
        [SerializeField] private bool flipVideoVertically = true;

        private Vector3 beaconBaseScale;
        private Button importButton;
        private Button playButton;
        private TMP_Text playButtonLabel;
        private TMP_Text videoStatusLabel;
        private TMP_Text timeStatusLabel;
        private Transform timelineProgress;
        private Renderer videoSurfaceRenderer;
        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        private Material videoMaterial;
        private int lastImportRequestFrame = -1;
        private bool isVideoReady;

        public event Action<string> VideoPathLoaded;

        public void Configure(Transform core, Transform beacon)
        {
            scanningCore = core;
            statusBeacon = beacon;
            beaconBaseScale = beacon != null ? beacon.localScale : Vector3.one;
        }

        private void Awake()
        {
            if (statusBeacon != null)
                beaconBaseScale = statusBeacon.localScale;

            ResolveSceneReferences();
            ConfigureVideoPlayer();
            SetPlaybackAvailable(false);
            UpdateTimeline(0f);
        }

        private void OnDestroy()
        {
            if (importButton != null)
                importButton.onClick.RemoveListener(SelectAndImportVideo);
            if (playButton != null)
                playButton.onClick.RemoveListener(TogglePlayback);

            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= HandleVideoPrepared;
                videoPlayer.errorReceived -= HandleVideoError;
                videoPlayer.loopPointReached -= HandleVideoFinished;
            }

            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }

            if (videoMaterial != null)
                Destroy(videoMaterial);
        }

        private void Update()
        {
            AnimateStudio();
            UpdateVideoPlayback();
        }

        private void ResolveSceneReferences()
        {
            var importTransform = FindDeepChild(transform, "IMPORT VIDEO Button");
            var playTransform = FindDeepChild(transform, "PLAY Button");
            importButton = importTransform != null ? importTransform.GetComponent<Button>() : null;
            playButton = playTransform != null ? playTransform.GetComponent<Button>() : null;
            playButtonLabel = playTransform != null
                ? playTransform.GetComponentInChildren<TMP_Text>(true)
                : null;

            var statusTransform = FindDeepChild(transform, "Demo Status");
            var timeTransform = FindDeepChild(transform, "Round Status");
            videoStatusLabel = statusTransform != null
                ? statusTransform.GetComponentInChildren<TMP_Text>(true)
                : null;
            timeStatusLabel = timeTransform != null
                ? timeTransform.GetComponentInChildren<TMP_Text>(true)
                : null;
            timelineProgress = FindDeepChild(transform, "Timeline Progress");

            var viewport = FindDeepChild(transform, "Tactical Viewport");
            videoSurfaceRenderer = viewport != null
                ? viewport.GetComponent<Renderer>()
                : null;

            var replayMarkers = FindDeepChild(transform, "Replay Markers");
            if (replayMarkers != null)
                replayMarkers.gameObject.SetActive(false);

            if (importButton != null)
                importButton.onClick.AddListener(SelectAndImportVideo);
            if (playButton != null)
                playButton.onClick.AddListener(TogglePlayback);
        }

        private void ConfigureVideoPlayer()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
                videoPlayer = gameObject.AddComponent<VideoPlayer>();

            videoTexture = new RenderTexture(
                videoTextureWidth,
                videoTextureHeight,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = "Match Video Render Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            videoTexture.Create();

            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.isLooping = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.prepareCompleted += HandleVideoPrepared;
            videoPlayer.errorReceived += HandleVideoError;
            videoPlayer.loopPointReached += HandleVideoFinished;

            if (videoSurfaceRenderer == null)
                return;

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("The Unlit/Texture shader is unavailable.");
                return;
            }

            videoMaterial = new Material(shader)
            {
                name = "Runtime Match Video Material",
                mainTexture = videoTexture
            };
            videoMaterial.mainTextureScale = new Vector2(
                flipVideoHorizontally ? -1f : 1f,
                flipVideoVertically ? -1f : 1f);
            videoMaterial.mainTextureOffset = new Vector2(
                flipVideoHorizontally ? 1f : 0f,
                flipVideoVertically ? 1f : 0f);
            videoSurfaceRenderer.material = videoMaterial;
        }

        private void SelectAndImportVideo()
        {
            if (lastImportRequestFrame == Time.frameCount)
                return;

            lastImportRequestFrame = Time.frameCount;
            SetVideoStatus("SELECTING VIDEO");
            Debug.Log("Import video button clicked.");
            var path = NativeVideoFilePicker.Pick();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetVideoStatus(isVideoReady ? "VIDEO READY" : "NO VIDEO");
                return;
            }

            LoadVideoFromPath(path);
        }

        public void LoadVideoFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetVideoStatus("VIDEO NOT FOUND");
                return;
            }

            var extension = Path.GetExtension(path);
            if (!IsSupportedVideoExtension(extension))
            {
                SetVideoStatus("UNSUPPORTED VIDEO");
                Debug.LogError("Unsupported video format: " + extension);
                return;
            }

            isVideoReady = false;
            videoPlayer.Stop();
            videoPlayer.url = new Uri(path).AbsoluteUri;
            VideoPathLoaded?.Invoke(path);
            SetPlaybackAvailable(false);
            SetVideoStatus("LOADING VIDEO");
            SetTimeStatus("00:00 // 00:00");
            UpdateTimeline(0f);
            if (scanningCore != null)
                scanningCore.gameObject.SetActive(true);

            Debug.Log("Preparing video: " + path);
            videoPlayer.Prepare();
        }

        private void HandleVideoPrepared(VideoPlayer player)
        {
            isVideoReady = true;
            player.time = 0d;
            SetPlaybackAvailable(true);
            SetVideoStatus("READY // " + Path.GetFileNameWithoutExtension(player.url).ToUpperInvariant());
            SetTimeStatus($"00:00 // {FormatTime(player.length)}");
            if (scanningCore != null)
                scanningCore.gameObject.SetActive(false);
            Debug.Log(
                $"Video prepared: {player.width}x{player.height}, " +
                $"{player.length:F1} seconds.");
        }

        private void HandleVideoError(VideoPlayer player, string message)
        {
            isVideoReady = false;
            SetPlaybackAvailable(false);
            SetVideoStatus("VIDEO ERROR");
            Debug.LogError("VideoPlayer error: " + message);
        }

        private void HandleVideoFinished(VideoPlayer player)
        {
            UpdatePlayButtonLabel();
            UpdateTimeline(1f);
        }

        private void TogglePlayback()
        {
            if (!isVideoReady)
                return;

            if (videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
            }
            else
            {
                if (videoPlayer.length > 0d && videoPlayer.time >= videoPlayer.length - 0.05d)
                    videoPlayer.time = 0d;
                videoPlayer.Play();
            }

            UpdatePlayButtonLabel();
        }

        private void UpdateVideoPlayback()
        {
            if (!isVideoReady || videoPlayer == null)
                return;

            var duration = Math.Max(0d, videoPlayer.length);
            var currentTime = Math.Max(0d, videoPlayer.time);
            var progress = duration > 0d
                ? Mathf.Clamp01((float)(currentTime / duration))
                : 0f;
            UpdateTimeline(progress);
            SetTimeStatus($"{FormatTime(currentTime)} // {FormatTime(duration)}");
            UpdatePlayButtonLabel();
        }

        private void UpdateTimeline(float progress)
        {
            if (timelineProgress == null)
                return;

            var width = Mathf.Max(0.04f, 6.4f * Mathf.Clamp01(progress));
            timelineProgress.localScale = new Vector3(
                width,
                timelineProgress.localScale.y,
                timelineProgress.localScale.z);
            timelineProgress.localPosition = new Vector3(
                -3.85f + width * 0.5f,
                timelineProgress.localPosition.y,
                timelineProgress.localPosition.z);
        }

        private void SetPlaybackAvailable(bool available)
        {
            if (playButton != null)
                playButton.interactable = available;
            UpdatePlayButtonLabel();
        }

        private void UpdatePlayButtonLabel()
        {
            if (playButtonLabel != null)
            {
                playButtonLabel.text =
                    videoPlayer != null && videoPlayer.isPlaying
                        ? "PAUSE"
                        : "PLAY";
            }
        }

        private void SetVideoStatus(string status)
        {
            if (videoStatusLabel != null)
                videoStatusLabel.text = status;
        }

        private void SetTimeStatus(string status)
        {
            if (timeStatusLabel != null)
                timeStatusLabel.text = status;
        }

        private void AnimateStudio()
        {
            if (scanningCore != null && scanningCore.gameObject.activeSelf)
            {
                scanningCore.Rotate(
                    Vector3.forward,
                    rotationSpeed * Time.unscaledDeltaTime,
                    Space.Self);
            }

            if (statusBeacon != null)
            {
                var pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
                statusBeacon.localScale = beaconBaseScale * pulse;
            }
        }

        private static bool IsSupportedVideoExtension(string extension)
        {
            foreach (var supportedExtension in SupportedVideoExtensions)
            {
                if (extension.Equals(supportedExtension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static Transform FindDeepChild(Transform parent, string objectName)
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

        private static string FormatTime(double seconds)
        {
            var totalSeconds = Math.Max(0, (int)Math.Floor(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }
    }
}
