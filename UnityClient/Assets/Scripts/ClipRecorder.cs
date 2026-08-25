using System;
using System.IO;
using System.Threading.Tasks;
using InstantReplay;
using UniEnc;
using UnityEngine;

namespace FpsAiCoach
{
    public enum ClipRecorderState
    {
        /// <summary>Nothing is encoding.</summary>
        Idle,

        /// <summary>The rolling buffer is live, so the last N seconds can be saved at any moment.</summary>
        Buffering,

        /// <summary>A full-length take is being written straight to disk.</summary>
        Recording,

        /// <summary>Muxing. The encoders are stopped but the file is not closed yet.</summary>
        Working,

        Saved,
        Failed
    }

    /// <summary>
    /// Records the war-room camera to an H.264 MP4, in either of two modes.
    ///
    /// The rolling buffer keeps the most recent seconds in memory so a moment can be saved after it
    /// happened, which is what makes this useful as a sparring aid: you notice the mistake, then keep
    /// the clip. The unbounded take writes continuously to disk for a whole practice session.
    ///
    /// The two modes never run at the same time. Each drives its own encoder and its own frame
    /// provider, and a provider re-renders the camera off-screen; running both would render and encode
    /// every frame twice for no benefit.
    /// </summary>
    public sealed class ClipRecorder : MonoBehaviour
    {
        [SerializeField] private WarRoomTheme theme;

        [Tooltip("Camera whose output is encoded. Falls back to Camera.main.")]
        [SerializeField] private Camera captureCamera;

        private RealtimeInstantReplaySession rolling;
        private UnboundedRecordingSession take;
        private string takePath;

        /// <summary>Raised whenever <see cref="State"/> or <see cref="StatusMessage"/> changes.</summary>
        public event Action StateChanged;

        public ClipRecorderState State { get; private set; } = ClipRecorderState.Idle;

        /// <summary>Display-ready description of the current state.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        /// <summary>Absolute path of the most recent finished file, or null before the first one.</summary>
        public string LastOutputPath { get; private set; }

        /// <summary>True while muxing, when no new command may start.</summary>
        public bool IsBusy => State == ClipRecorderState.Working;

        public bool IsRecordingTake => take != null;

        /// <summary>The rolling buffer must be live for a clip to exist to save.</summary>
        public bool CanSaveClip => rolling != null && !IsBusy;

        public void Configure(WarRoomTheme configuredTheme, Camera configuredCamera)
        {
            theme = configuredTheme;
            captureCamera = configuredCamera;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            if (Settings.bufferOnStart)
                StartBuffering();
            else
                SetState(ClipRecorderState.Idle, Copy.captureStatusIdle);
        }

        /// <summary>
        /// Covers both leaving play mode and the object being destroyed. An unbounded take holds an
        /// open MP4, so it is given a bounded chance to close before the encoders are torn down; the
        /// library's own guidance is that a take interrupted mid-write produces an unusable file.
        /// The wait is capped because shutdown must not hang if the muxer cannot finish.
        /// </summary>
        private void OnDisable()
        {
            if (take != null)
            {
                try
                {
                    take.CompleteAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"ClipRecorder: take did not close cleanly. {Flatten(exception.Message)}");
                }

                take.Dispose();
                take = null;
            }

            if (rolling != null)
            {
                // Nothing to salvage: the buffer only becomes a file through an explicit save.
                rolling.Dispose();
                rolling = null;
            }
        }

        // ------------------------------------------------------------------ public actions

        /// <summary>Starts the rolling buffer. Safe to call when it is already running.</summary>
        public void StartBuffering()
        {
            if (ArmBuffer(out var failure))
            {
                SetState(
                    ClipRecorderState.Buffering,
                    string.Format(Copy.captureStatusBuffering, Settings.clipSeconds));
                return;
            }

            if (failure != null)
                SetState(ClipRecorderState.Failed, failure);
        }

        /// <summary>
        /// Creates the rolling session without publishing a state, so a re-arm after a failed export
        /// cannot overwrite the message explaining that failure. Returns false with
        /// <paramref name="failure"/> set when the session could not be created, and false with null
        /// when there was simply nothing to do.
        /// </summary>
        private bool ArmBuffer(out string failure)
        {
            failure = null;

            if (rolling != null || take != null)
                return false;

            var camera = ResolveCamera();
            if (camera == null)
            {
                failure = string.Format(Copy.captureStatusUnavailable, "NO CAMERA");
                return false;
            }

            try
            {
                rolling = new RealtimeInstantReplaySession(
                    BuildOptions(),
                    CreateFrameProvider(camera),
                    true,
                    CreateAudioProvider(),
                    Settings.captureAudio,
                    HandlePipelineException);
                return true;
            }
            catch (Exception exception)
            {
                rolling = null;
                Debug.LogException(exception);
                failure = string.Format(Copy.captureStatusUnavailable, Flatten(exception.Message));
                return false;
            }
        }

        /// <summary>
        /// The camera path sees the 3D set and every world-space canvas, which covers the tactical
        /// screen, the rails, the deck and the AI overlay boxes. It does not see screen-space overlay
        /// canvases, so the corner brackets and the status strip stay out of the recording — which is
        /// usually wanted, since a burned-in "RECORDING" line is noise in the saved file.
        ///
        /// <c>captureFullScreen</c> switches to the composited frame instead, which also catches that
        /// overlay chrome. It reads the presented backbuffer, so unlike the camera path it captures
        /// nothing while the editor is not drawing the Game view; prefer it only in a player build.
        /// </summary>
        private IFrameProvider CreateFrameProvider(Camera camera)
        {
            var settings = Settings;

            if (settings.captureFullScreen)
                return new ScreenshotFrameProvider();

            return new CameraClipFrameProvider(
                camera,
                Mathf.Max(64, settings.resolution.x) & ~1,
                Mathf.Max(64, settings.resolution.y) & ~1,
                Mathf.Max(1, settings.frameRate));
        }

        /// <summary>
        /// Null lets the session bind its own provider to the scene's AudioListener. The encoding
        /// system always declares an audio track alongside the video one, so a track that never
        /// receives a sample makes the muxer fail to close the file; feeding it the listener's output
        /// keeps the track valid even when the war room is silent.
        /// </summary>
        private IAudioSampleProvider CreateAudioProvider()
        {
            return Settings.captureAudio ? null : NullAudioSampleProvider.Instance;
        }

        /// <summary>
        /// Writes the buffered tail to disk. The session can export only once and stops recording when
        /// it does, so a fresh buffer is started afterwards to keep the next moment catchable.
        /// </summary>
        public void SaveClip()
        {
            if (!CanSaveClip)
                return;

            _ = SaveClipAsync();
        }

        /// <summary>Starts an unbounded take, pausing the rolling buffer for its duration.</summary>
        public void StartTake()
        {
            if (take != null || IsBusy)
                return;

            var camera = ResolveCamera();
            if (camera == null)
            {
                SetState(
                    ClipRecorderState.Failed,
                    string.Format(Copy.captureStatusUnavailable, "NO CAMERA"));
                return;
            }

            // The buffer is dropped rather than paused: its contents belong to the moment before the
            // take began, and keeping it alive would double the encoding cost for frames nobody wants.
            if (rolling != null)
            {
                rolling.Dispose();
                rolling = null;
            }

            try
            {
                takePath = BuildOutputPath("Session");
                take = new UnboundedRecordingSession(
                    takePath,
                    BuildOptions(),
                    CreateFrameProvider(camera),
                    true,
                    CreateAudioProvider(),
                    Settings.captureAudio,
                    HandlePipelineException);
            }
            catch (Exception exception)
            {
                take = null;
                Debug.LogException(exception);
                var message = string.Format(Copy.captureStatusFailed, Flatten(exception.Message));
                ArmBuffer(out _);
                SetState(ClipRecorderState.Failed, message);
                return;
            }

            SetState(
                ClipRecorderState.Recording,
                string.Format(Copy.captureStatusRecording, Path.GetFileName(takePath)));
        }

        /// <summary>Closes the current take and resumes the rolling buffer.</summary>
        public void StopTake()
        {
            if (take == null || IsBusy)
                return;

            _ = StopTakeAsync();
        }

        /// <summary>Single entry point for a toggle button.</summary>
        public void ToggleTake()
        {
            if (take != null)
                StopTake();
            else
                StartTake();
        }

        // ------------------------------------------------------------------ async work

        private async Task SaveClipAsync()
        {
            var session = rolling;
            rolling = null;

            SetState(ClipRecorderState.Working, Copy.captureStatusSaving);

            string written = null;
            string failure = null;

            try
            {
                written = await session.StopAndExportAsync(
                    Settings.clipSeconds,
                    BuildOutputPath("Clip"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                failure = string.Format(Copy.captureStatusFailed, Flatten(exception.Message));
            }
            finally
            {
                session.Dispose();
            }

            Finish(written, failure);
        }

        private async Task StopTakeAsync()
        {
            var session = take;
            take = null;

            SetState(ClipRecorderState.Working, Copy.captureStatusSaving);

            string failure = null;

            try
            {
                await session.CompleteAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                failure = string.Format(Copy.captureStatusFailed, Flatten(exception.Message));
            }
            finally
            {
                session.Dispose();
            }

            Finish(failure == null ? takePath : null, failure);
        }

        /// <summary>
        /// Common tail for both modes: re-arm the buffer so capture continues, then publish the
        /// outcome. The buffer is armed first and its state deliberately not published, because
        /// re-arming must not overwrite the reason an export failed.
        /// </summary>
        private void Finish(string written, string failure)
        {
            ArmBuffer(out var armFailure);

            if (failure == null && !HasContent(written))
            {
                // The muxer can report success yet leave a zero-byte file behind when no frames reached
                // the encoder. Reporting that as "saved" would hand back a path that cannot be played.
                failure = Copy.captureStatusNoFrames;
                TryDelete(written);
                written = null;
            }

            if (failure != null)
            {
                SetState(ClipRecorderState.Failed, failure);
                return;
            }

            if (armFailure != null)
            {
                SetState(ClipRecorderState.Failed, armFailure);
                return;
            }

            LastOutputPath = written;
            SetState(
                ClipRecorderState.Saved,
                string.Format(Copy.captureStatusSaved, Path.GetFileName(written)));
        }

        private static bool HasContent(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var info = new FileInfo(path);
            return info.Exists && info.Length > 0L;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"ClipRecorder: could not remove empty output. {Flatten(exception.Message)}");
            }
        }

        // ------------------------------------------------------------------ configuration

        private RealtimeEncodingOptions BuildOptions()
        {
            var settings = Settings;
            var options = RealtimeEncodingOptions.Default;

            options.VideoOptions = new VideoEncoderOptions
            {
                // H.264 wants even dimensions, so odd values authored by hand are rounded down.
                Width = (uint)(Mathf.Max(64, settings.resolution.x) & ~1),
                Height = (uint)(Mathf.Max(64, settings.resolution.y) & ~1),
                FpsHint = (uint)Mathf.Max(1, settings.frameRate),
                Bitrate = (uint)Mathf.Max(200, settings.bitrateKbps) * 1000u
            };

            // The audio track must be described with the rate and channel count Unity actually
            // delivers. The library's defaults are 44100/2, but Unity follows the output device, which
            // is commonly 48000. Encoding a 48 kHz stream against a 44.1 kHz track description makes
            // the AAC encoder produce nothing, and the Windows MPEG4 sink then refuses to finalize the
            // file because a declared track never supplied its headers.
            options.AudioOptions = new AudioEncoderOptions
            {
                SampleRate = (uint)Mathf.Max(8000, AudioSettings.outputSampleRate),
                Channels = (uint)Mathf.Max(1, ChannelCount(AudioSettings.speakerMode)),
                Bitrate = options.AudioOptions.Bitrate
            };

            options.ForceReadback = settings.forceReadback;
            options.FixedFrameRate = Mathf.Max(1, settings.frameRate);
            options.MaxMemoryUsageBytesForCompressedFrames =
                (long)Mathf.Max(8, settings.bufferMegabytes) * 1024L * 1024L;

            return options;
        }

        private static int ChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        /// <summary>
        /// Clips land outside <c>Assets</c> by default so Unity never imports a multi-megabyte video
        /// as a project asset. Point <c>outputDirectory</c> at the backend's media root when a clip
        /// should be analyzable by the vision service straight after it is written.
        /// </summary>
        private string ResolveDirectory()
        {
            var configured = Settings.outputDirectory;
            if (string.IsNullOrWhiteSpace(configured))
                return Path.Combine(Application.persistentDataPath, "Clips");

            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(configured);
        }

        private string BuildOutputPath(string prefix)
        {
            var directory = ResolveDirectory();
            Directory.CreateDirectory(directory);
            return Path.Combine(
                directory,
                $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        }

        private Camera ResolveCamera()
        {
            if (captureCamera != null)
                return captureCamera;

            captureCamera = Camera.main;
            return captureCamera;
        }

        // ------------------------------------------------------------------ helpers

        private WarRoomTheme.Content Copy =>
            theme != null ? theme.Data : FallbackCopy;

        private WarRoomTheme.Capture Settings =>
            theme != null ? theme.Recording : FallbackSettings;

        private static WarRoomTheme.Content fallbackCopy;
        private static WarRoomTheme.Capture fallbackSettings;

        /// <summary>
        /// Keeps the component usable when it is added by hand without a theme, so a missing asset
        /// reference surfaces as default copy rather than a null reference mid-recording.
        /// </summary>
        private static WarRoomTheme.Content FallbackCopy =>
            fallbackCopy ??= new WarRoomTheme.Content();

        private static WarRoomTheme.Capture FallbackSettings =>
            fallbackSettings ??= new WarRoomTheme.Capture();

        /// <summary>
        /// Pipeline faults arrive from encoder threads, so this only records the message; the state
        /// change is left to whichever command is driving, which owns the UI-facing status.
        /// </summary>
        private void HandlePipelineException(Exception exception)
        {
            Debug.LogWarning($"ClipRecorder: encoding pipeline reported {Flatten(exception.Message)}");
        }

        private void SetState(ClipRecorderState state, string message)
        {
            State = state;
            StatusMessage = message ?? string.Empty;
            StateChanged?.Invoke();
        }

        /// <summary>The status line is one row of a narrow chrome strip, so long detail is truncated.</summary>
        private static string Flatten(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNKNOWN ERROR";

            var flat = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length <= 64 ? flat : flat.Substring(0, 63) + "…";
        }
    }
}
