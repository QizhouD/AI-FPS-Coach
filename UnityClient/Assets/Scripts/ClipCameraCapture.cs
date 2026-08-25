using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FpsAiCoach
{
    /// <summary>
    /// Renders a camera into an off-screen target on a fixed cadence and hands the pixels to whoever
    /// is listening.
    ///
    /// The camera is rendered on demand rather than read from the presented frame for two reasons. In
    /// the editor the Game view stops drawing as soon as it is hidden behind another tab or the window
    /// loses focus, and anything driven by <c>OnRenderImage</c> or by screen capture then silently
    /// receives nothing. And rendering into a fixed-size target decouples the recording from the
    /// window, so output dimensions come from the theme and a clip is reproducible.
    ///
    /// Only what this camera draws is captured, which covers the 3D set and every world-space canvas.
    /// Screen-space overlay canvases are drawn outside any camera and stay out of the recording, which
    /// is usually wanted since burned-in chrome is noise in a saved clip.
    ///
    /// Readback is asynchronous, so frames arrive a few frames after they were rendered. Requests
    /// complete in submission order, and the capture timestamp travels with each one so a consumer can
    /// place the frame on its own timeline.
    /// </summary>
    internal sealed class ClipCameraCapture : IDisposable
    {
        private readonly struct PendingFrame
        {
            public readonly AsyncGPUReadbackRequest Request;
            public readonly double CaptureTime;

            public PendingFrame(AsyncGPUReadbackRequest request, double captureTime)
            {
                Request = request;
                CaptureTime = captureTime;
            }
        }

        private readonly Camera source;
        private readonly double interval;
        private readonly Queue<PendingFrame> pending = new();

        private RenderTexture target;
        private double nextCaptureTime;

        /// <summary>Raised in capture order once a frame's pixels are readable.</summary>
        public event Action<NativeArray<byte>, double> FrameReady;

        public int Width { get; }

        public int Height { get; }

        public ClipCameraCapture(Camera source, int width, int height, int frameRate)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));

            // Kept even to match what the H.264 encoder accepts, so the capture target and the encoder
            // agree on dimensions without a rescale in between.
            Width = Mathf.Max(64, width) & ~1;
            Height = Mathf.Max(64, height) & ~1;
            interval = 1.0 / Mathf.Max(1, frameRate);

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.Default)
            {
                name = "ClipCameraCapture Target",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
        }

        /// <summary>
        /// Drains finished readbacks and possibly captures a new frame. Expected once per frame, at end
        /// of frame so the frame's canvas updates are included.
        /// </summary>
        public void Tick()
        {
            Drain(false);
            MaybeCapture();
        }

        private void MaybeCapture()
        {
            if (target == null || source == null)
                return;

            var now = Time.unscaledTimeAsDouble;
            if (now < nextCaptureTime)
                return;

            // Scheduled forward from now rather than from the previous slot, so a hitch does not leave a
            // backlog that renders several frames back to back trying to catch up.
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
                // target does not restore that. Without this the on-screen view would be subtly wrong
                // whenever the window is not the same shape as the capture target.
                source.ResetAspect();
            }

            pending.Enqueue(new PendingFrame(AsyncGPUReadback.Request(target), now));
        }

        /// <summary>
        /// Publishes completed readbacks. When <paramref name="block"/> is set the GPU is waited on,
        /// which is only wanted while closing a recording so the tail is not lost.
        /// </summary>
        private void Drain(bool block)
        {
            while (pending.Count > 0)
            {
                var frame = pending.Peek();

                if (block)
                    frame.Request.WaitForCompletion();
                else if (!frame.Request.done)
                    return;

                pending.Dequeue();

                if (frame.Request.hasError)
                    continue;

                FrameReady?.Invoke(frame.Request.GetData<byte>(), frame.CaptureTime);
            }
        }

        /// <summary>Waits for and publishes every outstanding frame, so a file can close complete.</summary>
        public void Flush()
        {
            Drain(true);
        }

        public void Dispose()
        {
            // Outstanding requests are abandoned rather than flushed: a caller that wants the tail calls
            // Flush first, and blocking here would stall teardown for frames nobody is going to write.
            pending.Clear();
            FrameReady = null;

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
