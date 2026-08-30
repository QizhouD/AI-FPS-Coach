using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace FpsAiCoach
{
    public enum ClipRecorderState
    {
        /// <summary>Nothing is encoding.</summary>
        Idle,

        /// <summary>The rolling buffer is live, so the recent past can be saved at any moment.</summary>
        Buffering,

        /// <summary>A full-length take is being written straight to disk.</summary>
        Recording,

        /// <summary>Closing a file. Brief, but no new command may start during it.</summary>
        Working,

        Saved,
        Failed
    }

    /// <summary>
    /// Records the war-room camera to an H.264 MP4, in either of two modes.
    ///
    /// The rolling buffer keeps recent history available so a moment can be saved after it happened,
    /// which is what makes this useful as a sparring aid: you notice the mistake, then keep the clip.
    /// The take writes continuously for a whole practice session.
    ///
    /// Because Unity's encoder writes forward only and cannot be asked for "the last N seconds", the
    /// rolling buffer is two segment files staggered by <c>clipSeconds</c>. Whichever segment is older
    /// has always been running for at least that long once the first interval has passed, so finishing
    /// it yields a clip covering *at least* the requested tail, up to twice it. Both segments share one
    /// camera render through <see cref="ClipCameraCapture"/>, so the second costs encoding only.
    ///
    /// The two modes never run at the same time, since a take would otherwise encode frames that the
    /// buffer is already encoding.
    /// </summary>
    public sealed class ClipRecorder : MonoBehaviour
    {
        [SerializeField] private WarRoomTheme theme;

        [Tooltip("Camera whose output is encoded. Falls back to Camera.main.")]
        [SerializeField] private Camera captureCamera;

        /// <summary>Raised whenever <see cref="State"/> or <see cref="StatusMessage"/> changes.</summary>
        public event Action StateChanged;

        public ClipRecorderState State { get; private set; } = ClipRecorderState.Idle;

        /// <summary>Display-ready description of the current state.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        /// <summary>Absolute path of the most recent finished file, or null before the first one.</summary>
        public string LastOutputPath { get; private set; }

        /// <summary>True while a file is closing, when no new command may start.</summary>
        public bool IsBusy => State == ClipRecorderState.Working;

        /// <summary>
        /// Whether recording can run at all in this build.
        ///
        /// Unity's MediaEncoder is an editor-only API, so a player build has nothing to encode
        /// with. Callers should hide the capture controls rather than present buttons that cannot
        /// do anything. This costs the shipped app nothing: practice footage is recorded with OBS,
        /// which is what the analysis pipeline consumes anyway.
        /// </summary>
#if UNITY_EDITOR
        public const bool IsAvailable = true;
#else
        public const bool IsAvailable = false;
#endif

#if UNITY_EDITOR
        private ClipCameraCapture capture;
        private readonly List<ClipMovieEncoder> segments = new();
        private ClipMovieEncoder take;
        private Coroutine pump;
        private int segmentSerial;
        private bool announcedCanSave;

        public bool IsRecordingTake => take != null;

        /// <summary>A segment must exist and hold at least one frame for a clip to be savable.</summary>
        public bool CanSaveClip => !IsBusy && OldestSegment() is { FramesWritten: > 0 };
#else
        public bool IsRecordingTake => false;

        public bool CanSaveClip => false;
#endif

        public void Configure(WarRoomTheme configuredTheme, Camera configuredCamera)
        {
            theme = configuredTheme;
            captureCamera = configuredCamera;
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Arming lives here rather than in Start so that it is symmetric with <see cref="OnDisable"/>,
        /// which tears the encoder and the buffer down. Editing any script during play triggers a domain
        /// reload, and that calls OnDisable without ever calling Start again — the recorder would then sit
        /// there reporting BUFFERING with no capture running and silently refuse to save.
        /// </summary>
        private void OnEnable()
        {
#if UNITY_EDITOR
            if (Settings.bufferOnStart)
                StartBuffering();
            else
                SetState(ClipRecorderState.Idle, Copy.captureStatusIdle);
#else
            // Unity's encoder is an editor API, so there is nothing to fall back to in a player build.
            SetState(
                ClipRecorderState.Failed,
                string.Format(Copy.captureStatusUnavailable, "EDITOR ONLY"));
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Covers both leaving play mode and the object being destroyed. An open MP4 is unusable until
        /// its index is written, so every file is closed here; the take is kept because it is a
        /// deliberate recording, while rolling segments are discarded because nobody asked for them.
        /// </summary>
        private void OnDisable()
        {
            StopPump();
            capture?.Flush();

            if (take != null)
            {
                take.Finish();
                LastOutputPath = take.Path;
                take = null;
            }

            foreach (var segment in segments)
                segment.Discard();

            segments.Clear();

            capture?.Dispose();
            capture = null;
        }

        // ------------------------------------------------------------------ public actions

        /// <summary>Starts the rolling buffer. Safe to call when it is already running.</summary>
        public void StartBuffering()
        {
            if (take != null || segments.Count > 0)
                return;

            if (!EnsureCapture(out var failure))
            {
                SetState(ClipRecorderState.Failed, failure);
                return;
            }

            OpenSegment();
            SetState(
                ClipRecorderState.Buffering,
                string.Format(Copy.captureStatusBuffering, Settings.clipSeconds));
        }

        /// <summary>
        /// Finishes the oldest rolling segment and keeps it. A replacement is opened immediately so the
        /// next moment stays catchable.
        /// </summary>
        public void SaveClip()
        {
            if (!CanSaveClip)
                return;

            SetState(ClipRecorderState.Working, Copy.captureStatusSaving);

            var segment = OldestSegment();
            segments.Remove(segment);

            capture.Flush();
            segment.Finish();

            var destination = BuildOutputPath("Clip");
            var written = TryPublish(segment.Path, destination, out var failure);

            OpenSegment();
            Finish(written ? destination : null, failure);
        }

        /// <summary>Starts a continuous take, dropping the rolling buffer for its duration.</summary>
        public void StartTake()
        {
            if (take != null || IsBusy)
                return;

            if (!EnsureCapture(out var failure))
            {
                SetState(ClipRecorderState.Failed, failure);
                return;
            }

            // The buffer is dropped rather than kept: its contents belong to the moment before the take
            // began, and keeping it alive would double the encoding cost for frames nobody wants.
            DiscardSegments();

            var settings = Settings;
            take = new ClipMovieEncoder(
                BuildOutputPath("Session"),
                capture.Width,
                capture.Height,
                settings.frameRate);

            SetState(
                ClipRecorderState.Recording,
                string.Format(Copy.captureStatusRecording, Path.GetFileName(take.Path)));
        }

        /// <summary>Closes the current take and resumes the rolling buffer.</summary>
        public void StopTake()
        {
            if (take == null || IsBusy)
                return;

            SetState(ClipRecorderState.Working, Copy.captureStatusSaving);

            var closing = take;
            take = null;

            capture.Flush();
            var frames = closing.Finish();

            OpenSegment();
            Finish(frames > 0 ? closing.Path : null, null);
        }

        /// <summary>Single entry point for a toggle button.</summary>
        public void ToggleTake()
        {
            if (take != null)
                StopTake();
            else
                StartTake();
        }

        // ------------------------------------------------------------------ capture pump

        private bool EnsureCapture(out string failure)
        {
            failure = null;

            if (capture != null)
                return true;

            var camera = ResolveCamera();
            if (camera == null)
            {
                failure = string.Format(Copy.captureStatusUnavailable, "NO CAMERA");
                return false;
            }

            var settings = Settings;

            try
            {
                capture = new ClipCameraCapture(
                    camera,
                    settings.resolution.x,
                    settings.resolution.y,
                    settings.frameRate);
            }
            catch (Exception exception)
            {
                capture = null;
                Debug.LogException(exception);
                failure = string.Format(Copy.captureStatusUnavailable, Flatten(exception.Message));
                return false;
            }

            capture.FrameReady += HandleFrameReady;
            pump = StartCoroutine(Pump());
            return true;
        }

        /// <summary>
        /// End of frame is used so a captured frame includes the canvas updates of the frame it belongs
        /// to, and because it keeps running even when nothing is being presented to a display.
        /// </summary>
        private IEnumerator Pump()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                capture?.Tick();
            }
        }

        private void StopPump()
        {
            if (pump == null)
                return;

            StopCoroutine(pump);
            pump = null;
        }

        private void HandleFrameReady(NativeArray<byte> data, double captureTime)
        {
            if (take != null)
            {
                take.AddFrame(data, captureTime);
                return;
            }

            for (var i = 0; i < segments.Count; i++)
                segments[i].AddFrame(data, captureTime);

            RecycleAgedSegment(captureTime);
            AnnounceSavability();
        }

        /// <summary>
        /// Savability turns on when the first frame reaches a fresh segment, which is not a state
        /// transition and so raises nothing on its own. Without this the HUD would latch SAVE CLIP
        /// disabled from the moment buffering started and never re-enable it.
        /// </summary>
        private void AnnounceSavability()
        {
            var canSave = CanSaveClip;
            if (canSave == announcedCanSave)
                return;

            announcedCanSave = canSave;
            StateChanged?.Invoke();
        }

        // ------------------------------------------------------------------ rolling segments

        /// <summary>
        /// Keeps the staggered pair going: a second segment opens once the first is <c>clipSeconds</c>
        /// old, and from then on the older of the two is replaced every <c>clipSeconds</c>. That leaves
        /// the survivor always holding between one and two times the requested tail.
        /// </summary>
        private void RecycleAgedSegment(double now)
        {
            var window = Mathf.Max(1, Settings.clipSeconds);

            if (segments.Count == 0)
                return;

            if (segments.Count == 1)
            {
                if (SegmentAge(segments[0], now) >= window)
                    OpenSegment();

                return;
            }

            var oldest = OldestSegment();
            if (SegmentAge(oldest, now) >= window * 2)
            {
                segments.Remove(oldest);
                oldest.Discard();
                OpenSegment();
            }
        }

        private static double SegmentAge(ClipMovieEncoder segment, double now)
        {
            // Age is measured from the first frame actually written, not from construction, so a segment
            // is never considered old enough on the strength of frames it does not have.
            return double.IsNaN(segment.FirstFrameTime) ? 0.0 : now - segment.FirstFrameTime;
        }

        private ClipMovieEncoder OldestSegment()
        {
            ClipMovieEncoder oldest = null;

            foreach (var segment in segments)
            {
                if (segment.FramesWritten == 0)
                    continue;

                if (oldest == null || segment.FirstFrameTime < oldest.FirstFrameTime)
                    oldest = segment;
            }

            // Falls back to any segment so callers can still reason about an unstarted buffer.
            return oldest ?? (segments.Count > 0 ? segments[0] : null);
        }

        private void OpenSegment()
        {
            var settings = Settings;

            // Segments live in a scratch folder so a half-written rolling file is never mistaken for a
            // saved clip, and are serialised so two open segments cannot collide within the same second.
            var path = Path.Combine(
                ResolveDirectory(),
                "Buffer",
                $"Segment_{DateTime.Now:yyyyMMdd_HHmmss}_{segmentSerial++}.mp4");

            segments.Add(new ClipMovieEncoder(path, capture.Width, capture.Height, settings.frameRate));
        }

        private void DiscardSegments()
        {
            foreach (var segment in segments)
                segment.Discard();

            segments.Clear();
        }

        // ------------------------------------------------------------------ output

        /// <summary>
        /// Moves a finished segment out of the scratch folder. Reports failure rather than throwing so a
        /// full disk surfaces on the status line instead of breaking the recorder.
        /// </summary>
        private static bool TryPublish(string source, string destination, out string failure)
        {
            failure = null;

            try
            {
                // An empty segment is left unmoved and reported without a reason, so the caller's own
                // empty-file check names it rather than this method guessing at the wording.
                var info = new FileInfo(source);
                if (!info.Exists || info.Length == 0L)
                    return false;

                Directory.CreateDirectory(Path.GetDirectoryName(destination));

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Move(source, destination);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                failure = Flatten(exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Common tail for both modes: publish the outcome, treating an empty file as a failure since
        /// reporting it as saved would hand back a path that cannot be played.
        /// </summary>
        private void Finish(string written, string failure)
        {
            if (failure != null)
            {
                SetState(ClipRecorderState.Failed, string.Format(Copy.captureStatusFailed, failure));
                return;
            }

            if (!HasContent(written))
            {
                SetState(ClipRecorderState.Failed, Copy.captureStatusNoFrames);
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
#endif

        // ------------------------------------------------------------------ configuration

        /// <summary>
        /// Clips land outside <c>Assets</c> by default so Unity never imports a multi-megabyte video as
        /// a project asset. Point <c>outputDirectory</c> at the backend's media root when a clip should
        /// be analyzable by the vision service straight after it is written.
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
            return Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        }

        private Camera ResolveCamera()
        {
            if (captureCamera != null)
                return captureCamera;

            captureCamera = Camera.main;
            return captureCamera;
        }

        // ------------------------------------------------------------------ helpers

        private WarRoomTheme.Content Copy => theme != null ? theme.Data : FallbackCopy;

        private WarRoomTheme.Capture Settings => theme != null ? theme.Recording : FallbackSettings;

        private static WarRoomTheme.Content fallbackCopy;
        private static WarRoomTheme.Capture fallbackSettings;

        /// <summary>
        /// Keeps the component usable when it is added by hand without a theme, so a missing asset
        /// reference surfaces as default copy rather than a null reference mid-recording.
        /// </summary>
        private static WarRoomTheme.Content FallbackCopy => fallbackCopy ??= new WarRoomTheme.Content();

        private static WarRoomTheme.Capture FallbackSettings => fallbackSettings ??= new WarRoomTheme.Capture();

        private void SetState(ClipRecorderState state, string message)
        {
            State = state;
            StatusMessage = message ?? string.Empty;

#if UNITY_EDITOR
            // Recorded here too, so the frame-driven check does not raise a second event for a change
            // this transition has already published.
            announcedCanSave = CanSaveClip;
#endif

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
