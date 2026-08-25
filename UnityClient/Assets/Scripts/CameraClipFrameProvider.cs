using System;
using System.Threading;
using InstantReplay;
using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Supplies encoder frames by rendering a camera into an off-screen RenderTexture at a fixed
    /// cadence.
    ///
    /// This exists instead of <see cref="BuiltinCameraFrameProvider"/> because that provider is driven
    /// by <c>OnRenderImage</c>, so it only receives frames while something is actually drawing the
    /// camera. In the editor the Game view stops rendering as soon as it is hidden behind another tab
    /// or the window loses focus, and the video track then silently receives nothing while the audio
    /// track keeps filling — which ends with the muxer refusing to close the file, since a declared
    /// track never supplied its headers. Calling <see cref="Camera.Render"/> directly forces the draw
    /// regardless of what the editor is showing.
    ///
    /// Rendering into a fixed-size target also decouples the recording from the window: output
    /// dimensions come from the theme rather than <c>Screen.width</c>, so a clip is reproducible.
    ///
    /// Only what this camera draws is captured, which covers the 3D set and every world-space canvas.
    /// Screen-space overlay canvases are drawn outside any camera and so stay out of the recording.
    /// </summary>
    internal sealed class CameraClipFrameProvider : IFrameProvider
    {
        private readonly Camera source;
        private readonly double interval;
        private readonly CancellationTokenSource cancelOnDispose = new();

        private RenderTexture target;
        private double nextCaptureTime;

        public event IFrameProvider.ProvideFrame OnFrameProvided;

        public CameraClipFrameProvider(Camera source, int width, int height, int frameRate)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            interval = 1.0 / Mathf.Max(1, frameRate);

            target = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                // Named for the frame debugger and memory profiler.
                name = "ClipRecorder CameraClipFrameProvider",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();

            _ = CaptureLoop();
        }

        /// <summary>
        /// End of frame is used so the capture sees the same state the player loop just finished
        /// producing, and because it keeps ticking even when nothing is being presented to a display.
        /// </summary>
        private async Awaitable CaptureLoop()
        {
            var token = cancelOnDispose.Token;

            while (true)
            {
                // The token is deliberately not passed in: doing so allocates on every iteration.
                await Awaitable.EndOfFrameAsync();

                if (token.IsCancellationRequested)
                    return;

                try
                {
                    Capture();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void Capture()
        {
            if (target == null || source == null)
                return;

            var now = Time.unscaledTimeAsDouble;
            if (now < nextCaptureTime)
                return;

            // Scheduled forward from now rather than from the previous slot, so a hitch does not leave
            // a backlog that then renders several frames back to back to catch up.
            nextCaptureTime = now + interval;

            var previousTarget = source.targetTexture;
            source.targetTexture = target;

            try
            {
                source.Render();
            }
            finally
            {
                source.targetTexture = previousTarget;

                // Pointing a camera at a render texture overwrites its aspect ratio, and restoring the
                // target does not restore that. Without this the camera keeps the 16:9 of the capture
                // target and the on-screen view is subtly wrong whenever the window is not 16:9.
                source.ResetAspect();
            }

            // False matches BuiltinCameraFrameProvider: a camera-rendered target holds its first row at
            // the bottom, unlike ScreenCapture.CaptureScreenshotIntoRenderTexture.
            OnFrameProvided?.Invoke(new IFrameProvider.Frame(target, now, false));
        }

        public void Dispose()
        {
            cancelOnDispose.Cancel();

            if (target == null)
                return;

            var releasing = target;
            target = null;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(releasing);
            else
                UnityEngine.Object.DestroyImmediate(releasing);
        }
    }
}
