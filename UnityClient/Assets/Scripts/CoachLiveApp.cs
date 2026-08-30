using System;
using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Owns the capture device that feeds the war room's live view.
    ///
    /// Live mode is display only. There is deliberately no in-game analysis: the product analyses
    /// a recording after the round, because a coaching cue that arrives mid-duel is both too late
    /// to act on and impossible to read while playing.
    /// </summary>
    public sealed class CoachLiveApp : MonoBehaviour
    {
        private WebCamTexture source;
        private string[] devices = Array.Empty<string>();
        private int selectedDevice;
        private string connectionState = "Not started";

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

        public void RefreshDevices()
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
        }

        private void StopSource()
        {
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
        }
    }
}
