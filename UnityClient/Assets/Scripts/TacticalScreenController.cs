using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace FpsAiCoach
{
    public enum TacticalScreenState
    {
        Idle,
        Selecting,
        Loading,
        Ready,
        Error,
        Unsupported,
        Missing,
        Live
    }

    /// <summary>
    /// Owns the hero display surface: the render texture, the unlit material swap, video decoding
    /// and the live-capture handoff. Deliberately free of UI concerns so the HUD can render its
    /// state however it likes.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class TacticalScreenController : MonoBehaviour
    {
        private static readonly string[] SupportedExtensions =
        {
            ".mp4", ".mov", ".webm", ".avi", ".m4v"
        };

        [Header("Surface")]
        [SerializeField] private Renderer surfaceRenderer;

        [Header("Render texture")]
        [SerializeField] private int textureWidth = 1920;
        [SerializeField] private int textureHeight = 1080;

        // The display slab is a cube seen from behind, so its -Z face carries mirrored UVs.
        // Both flips cancel that out; without the horizontal one the footage is mirrored while
        // the detection overlay is not, and every box lands on the wrong side of the frame.
        [Header("Orientation")]
        [SerializeField] private bool flipVideoHorizontally = true;
        [SerializeField] private bool flipVideoVertically = true;
        [SerializeField] private bool flipLiveHorizontally = true;
        [SerializeField] private bool flipLiveVertically = true;

        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        private Material surfaceMaterial;
        private bool isVideoReady;
        private bool liveMode;

        /// <summary>Raised with the absolute path whenever a new clip starts preparing.</summary>
        public event Action<string> VideoPathLoaded;

        /// <summary>Raised whenever <see cref="State"/> or <see cref="SourceLabel"/> changes.</summary>
        public event Action StateChanged;

        public TacticalScreenState State { get; private set; } = TacticalScreenState.Idle;

        /// <summary>Human-readable name of the active source, or empty when there is none.</summary>
        public string SourceLabel { get; private set; } = string.Empty;

        public VideoPlayer Player => videoPlayer;
        public RenderTexture VideoTexture => videoTexture;
        public bool IsVideoReady => isVideoReady;
        public bool IsLiveMode => liveMode;
        public bool IsPlaying => videoPlayer != null && videoPlayer.isPlaying;

        public double Duration => videoPlayer != null ? Math.Max(0d, videoPlayer.length) : 0d;
        public double CurrentTime => videoPlayer != null ? Math.Max(0d, videoPlayer.time) : 0d;

        public float NormalizedProgress
        {
            get
            {
                var duration = Duration;
                return duration > 0d
                    ? Mathf.Clamp01((float)(CurrentTime / duration))
                    : 0f;
            }
        }

        private void Awake()
        {
            // Created in Awake so VisionInferenceOverlay can read targetTexture during Start.
            videoPlayer = GetComponent<VideoPlayer>();
            CreateRenderTexture();
            ConfigureVideoPlayer();
            CreateSurfaceMaterial();
            SetState(TacticalScreenState.Idle, string.Empty);
        }

        /// <summary>
        /// The player's callbacks are subscribed here rather than alongside the rest of its setup because
        /// a domain reload — which any script edit during play triggers — drops C# delegates while leaving
        /// the component itself alive, and Awake does not run a second time. Subscribing on enable instead
        /// keeps playback from going deaf to its own prepare and error notifications.
        /// </summary>
        private void OnEnable()
        {
            if (videoPlayer == null)
                return;

            videoPlayer.prepareCompleted += HandlePrepared;
            videoPlayer.errorReceived += HandleError;
            videoPlayer.loopPointReached += HandleFinished;
        }

        private void OnDisable()
        {
            if (videoPlayer == null)
                return;

            videoPlayer.prepareCompleted -= HandlePrepared;
            videoPlayer.errorReceived -= HandleError;
            videoPlayer.loopPointReached -= HandleFinished;
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }

            if (surfaceMaterial != null)
                Destroy(surfaceMaterial);
        }

        private void CreateRenderTexture()
        {
            videoTexture = new RenderTexture(
                textureWidth,
                textureHeight,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = "Tactical Screen RT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            videoTexture.Create();
        }

        private void ConfigureVideoPlayer()
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.isLooping = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        }

        private void CreateSurfaceMaterial()
        {
            if (surfaceRenderer == null)
            {
                Debug.LogWarning(
                    "TacticalScreenController: no surface renderer assigned, the screen stays blank.");
                return;
            }

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("TacticalScreenController: the Unlit/Texture shader is unavailable.");
                return;
            }

            surfaceMaterial = new Material(shader) { name = "Tactical Screen (Runtime)" };
            surfaceRenderer.material = surfaceMaterial;
            ApplySource(videoTexture, flipVideoHorizontally, flipVideoVertically);
        }

        private void ApplySource(Texture texture, bool flipX, bool flipY)
        {
            if (surfaceMaterial == null)
                return;

            surfaceMaterial.mainTexture = texture;
            surfaceMaterial.mainTextureScale = new Vector2(flipX ? -1f : 1f, flipY ? -1f : 1f);
            surfaceMaterial.mainTextureOffset = new Vector2(flipX ? 1f : 0f, flipY ? 1f : 0f);
        }

        // ------------------------------------------------------------------ video path

        /// <summary>Opens the native picker and loads the chosen clip. Returns false when cancelled.</summary>
        public bool SelectAndLoadVideo()
        {
            SetState(TacticalScreenState.Selecting, SourceLabel);
            var path = NativeVideoFilePicker.Pick();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetState(
                    isVideoReady ? TacticalScreenState.Ready : TacticalScreenState.Idle,
                    SourceLabel);
                return false;
            }

            LoadVideo(path);
            return true;
        }

        public void LoadVideo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetState(TacticalScreenState.Missing, string.Empty);
                return;
            }

            if (!IsSupported(Path.GetExtension(path)))
            {
                SetState(TacticalScreenState.Unsupported, string.Empty);
                return;
            }

            ExitLiveMode();

            isVideoReady = false;
            videoPlayer.Stop();
            videoPlayer.url = new Uri(path).AbsoluteUri;
            SetState(
                TacticalScreenState.Loading,
                Path.GetFileNameWithoutExtension(path).ToUpperInvariant());
            VideoPathLoaded?.Invoke(path);
            videoPlayer.Prepare();
        }

        /// <summary>Starts or pauses playback. Returns true when the screen is now playing.</summary>
        public bool TogglePlayback()
        {
            if (!isVideoReady || liveMode)
                return false;

            if (videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
                return false;
            }

            if (videoPlayer.length > 0d && videoPlayer.time >= videoPlayer.length - 0.05d)
                videoPlayer.time = 0d;
            videoPlayer.Play();
            return true;
        }

        private void HandlePrepared(VideoPlayer player)
        {
            isVideoReady = true;
            player.time = 0d;
            SetState(
                TacticalScreenState.Ready,
                Path.GetFileNameWithoutExtension(player.url).ToUpperInvariant());
        }

        private void HandleError(VideoPlayer player, string message)
        {
            isVideoReady = false;
            SetState(TacticalScreenState.Error, string.Empty);
            Debug.LogError("TacticalScreenController: video error - " + message);
        }

        private void HandleFinished(VideoPlayer player)
        {
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ live path

        /// <summary>Routes a live capture texture (usually a WebCamTexture) onto the surface.</summary>
        public void EnterLiveMode(Texture liveTexture, string label)
        {
            if (liveTexture == null)
                return;

            if (videoPlayer.isPlaying)
                videoPlayer.Pause();

            liveMode = true;
            ApplySource(liveTexture, flipLiveHorizontally, flipLiveVertically);
            SetState(TacticalScreenState.Live, string.IsNullOrEmpty(label) ? string.Empty : label);
        }

        /// <summary>Returns the surface to the video render texture.</summary>
        public void ExitLiveMode()
        {
            if (!liveMode)
                return;

            liveMode = false;
            ApplySource(videoTexture, flipVideoHorizontally, flipVideoVertically);
            SetState(
                isVideoReady ? TacticalScreenState.Ready : TacticalScreenState.Idle,
                isVideoReady ? SourceLabel : string.Empty);
        }

        private void SetState(TacticalScreenState state, string label)
        {
            var changed = State != state || SourceLabel != (label ?? string.Empty);
            State = state;
            SourceLabel = label ?? string.Empty;
            if (changed)
                StateChanged?.Invoke();
        }

        private static bool IsSupported(string extension)
        {
            foreach (var supported in SupportedExtensions)
            {
                if (extension.Equals(supported, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
