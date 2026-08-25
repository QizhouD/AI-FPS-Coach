#if UNITY_EDITOR
using System;
using System.IO;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Writes captured frames to an H.264 MP4 using Unity's own bundled encoder.
    ///
    /// This exists because InstantReplay's encoder cannot start on this machine: its native Windows
    /// backend finds no usable H.264 Media Foundation transform (it logs "Skipping MFT" for both Intel
    /// Quick Sync and the software encoder while accepting AAC), so the video track stays empty and the
    /// muxer then refuses to close the file. Unity's encoder does not go through Media Foundation and
    /// produces a valid file on the same machine.
    ///
    /// Frames arrive from a shared <see cref="ClipCameraCapture"/> so that several encoders — the two
    /// staggered segments behind the rolling buffer — cost one camera render between them rather than
    /// one each.
    ///
    /// Editor only: <see cref="MediaEncoder"/> lives in UnityEditor and is unavailable to a player
    /// build, so <see cref="ClipRecorder"/> reports capture as unavailable there instead of pretending.
    /// </summary>
    internal sealed class ClipMovieEncoder : IDisposable
    {
        private readonly int width;
        private readonly int height;
        private readonly int frameRate;

        private MediaEncoder encoder;
        private double firstFrameTime = double.NaN;
        private long framesWritten;

        /// <summary>Absolute path being written. Stays valid after <see cref="Finish"/>.</summary>
        public string Path { get; }

        /// <summary>Capture time of the first frame, or NaN before one arrives.</summary>
        public double FirstFrameTime => firstFrameTime;

        public long FramesWritten => framesWritten;

        public bool IsOpen => encoder != null;

        /// <summary>Recorded length in seconds, derived from the frames actually written.</summary>
        public double DurationSeconds => framesWritten / (double)frameRate;

        public ClipMovieEncoder(string path, int width, int height, int frameRate)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));

            // H.264 requires even dimensions.
            this.width = Mathf.Max(64, width) & ~1;
            this.height = Mathf.Max(64, height) & ~1;
            this.frameRate = Mathf.Max(1, frameRate);

            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var attributes = new VideoTrackAttributes
            {
                frameRate = new MediaRational(this.frameRate),
                width = (uint)this.width,
                height = (uint)this.height,
                includeAlpha = false
            };

            // No audio track is declared. A declared track that never receives samples is exactly what
            // made the previous encoder fail to finalize, and the war room has nothing worth recording.
            encoder = new MediaEncoder(path, attributes);
        }

        /// <summary>
        /// Appends one captured frame. <paramref name="captureTime"/> is unscaled time at capture and
        /// must not go backwards between calls.
        /// </summary>
        public void AddFrame(NativeArray<byte> data, double captureTime)
        {
            if (encoder == null)
                return;

            if (double.IsNaN(firstFrameTime))
                firstFrameTime = captureTime;

            // The encoder advances by exactly one frame interval per AddFrame, so playback would run
            // fast if the editor captured slower than the target rate. Placing each frame at the index
            // its capture time implies, and repeating it to fill any gap, keeps the played duration
            // equal to the recorded wall-clock duration.
            var targetIndex = (long)Math.Round((captureTime - firstFrameTime) * frameRate);

            // Cap the repeat so a long editor stall cannot expand into thousands of identical frames,
            // and always write at least once so jitter landing on a used slot still stores the frame.
            var limit = Math.Min(targetIndex, framesWritten + frameRate);

            do
            {
                encoder.AddFrame(width, height, width * 4, TextureFormat.RGBA32, data);
                framesWritten++;
            }
            while (framesWritten <= limit);
        }

        /// <summary>Closes the file. Safe to call more than once. Returns the frames written.</summary>
        public long Finish()
        {
            if (encoder == null)
                return framesWritten;

            encoder.Dispose();
            encoder = null;
            return framesWritten;
        }

        /// <summary>Closes and deletes the file, for a rolling segment that aged out unused.</summary>
        public void Discard()
        {
            Finish();

            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"ClipMovieEncoder: could not delete {Path}. {exception.Message}");
            }
        }

        public void Dispose()
        {
            Finish();
        }
    }
}
#endif
