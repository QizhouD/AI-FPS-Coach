using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FpsAiCoach
{
    public enum DemoAnalysisState
    {
        Idle,
        Selecting,
        Requesting,
        Ready,
        Rejected,
        Failed
    }

    /// <summary>
    /// Owns the CS2 demo analysis exchange for the war room: picking a <c>.dem</c> file, posting it to
    /// the local FastAPI service, loading the built-in sample report, and exposing the parsed result.
    ///
    /// This is the only place in the scene that talks to the demo endpoints. The HUD subscribes to
    /// <see cref="StateChanged"/> and <see cref="ReportLoaded"/> rather than polling, so the rails and
    /// header stay in step with the request without either side knowing about the other's layout.
    /// </summary>
    public sealed class DemoAnalysisController : MonoBehaviour
    {
        [SerializeField] private WarRoomTheme theme;

        private Coroutine active;

        /// <summary>Raised whenever <see cref="State"/> or <see cref="StatusMessage"/> changes.</summary>
        public event Action StateChanged;

        /// <summary>Raised after a report parses successfully.</summary>
        public event Action<DemoReport> ReportLoaded;

        public DemoAnalysisState State { get; private set; } = DemoAnalysisState.Idle;

        /// <summary>Short, display-ready description of the current state.</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        /// <summary>The most recent successful report, or null before the first one lands.</summary>
        public DemoReport Report { get; private set; }

        public bool IsBusy =>
            State == DemoAnalysisState.Selecting || State == DemoAnalysisState.Requesting;

        public void Configure(WarRoomTheme configuredTheme)
        {
            theme = configuredTheme;
        }

        private void Start()
        {
            if (State == DemoAnalysisState.Idle && string.IsNullOrEmpty(StatusMessage))
                SetState(DemoAnalysisState.Idle, Copy.demoStatusIdle);
        }

        // ------------------------------------------------------------------ public actions

        /// <summary>Opens the native picker and analyzes the chosen demo.</summary>
        public void SelectAndAnalyze()
        {
            if (IsBusy)
                return;

            SetState(DemoAnalysisState.Selecting, Copy.demoStatusSelecting);

            var path = NativeDemoFilePicker.Pick();
            if (string.IsNullOrEmpty(path))
            {
                SetState(DemoAnalysisState.Idle, Copy.demoStatusIdle);
                return;
            }

            Analyze(path);
        }

        /// <summary>Analyzes a demo already on disk. Validates before touching the network.</summary>
        public void Analyze(string path)
        {
            if (IsBusy && State != DemoAnalysisState.Selecting)
                return;

            if (!File.Exists(path))
            {
                SetState(DemoAnalysisState.Rejected, Copy.demoStatusMissing);
                return;
            }

            var file = new FileInfo(path);
            if (!file.Extension.Equals(".dem", StringComparison.OrdinalIgnoreCase))
            {
                SetState(DemoAnalysisState.Rejected, Copy.demoStatusWrongType);
                return;
            }

            var capBytes = (long)Copy.demoMaxUploadMegabytes * 1024L * 1024L;
            if (file.Length > capBytes)
            {
                // Naming both numbers matters: two of the reference demos in this project sit just
                // over the cap, and "too large" alone leaves the user guessing by how much.
                SetState(
                    DemoAnalysisState.Rejected,
                    string.Format(
                        Copy.demoStatusTooLarge,
                        Megabytes(file.Length),
                        Copy.demoMaxUploadMegabytes));
                return;
            }

            active = StartCoroutine(PostDemo(file));
        }

        /// <summary>Fetches the service's built-in sample report, which needs no file and no models.</summary>
        public void LoadSample()
        {
            if (IsBusy)
                return;

            active = StartCoroutine(GetSample());
        }

        // ------------------------------------------------------------------ requests

        /// <summary>Read granularity while filling the request body, and how often the coroutine yields.</summary>
        private const int ReadChunkBytes = 4 * 1024 * 1024;

        private IEnumerator PostDemo(FileInfo file)
        {
            SetState(
                DemoAnalysisState.Requesting,
                string.Format(Copy.demoStatusReading, file.Name));

            var boundary = "----FpsAiCoach" + Guid.NewGuid().ToString("N");
            var header =
                "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"target_player\"\r\n\r\n" +
                Copy.demoTargetPlayer.Trim() + "\r\n" +
                "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"demo\"; filename=\"" + file.Name + "\"\r\n" +
                "Content-Type: application/octet-stream\r\n\r\n";
            var footer = "\r\n--" + boundary + "--\r\n";

            var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            var footerBytes = System.Text.Encoding.UTF8.GetBytes(footer);
            var total = headerBytes.LongLength + file.Length + footerBytes.LongLength;

            if (total > int.MaxValue)
            {
                SetState(DemoAnalysisState.Rejected, Copy.demoStatusTooLargeForBuffer);
                active = null;
                yield break;
            }

            // The body is assembled by hand rather than through UnityWebRequest.Post's multipart
            // overload. That overload runs SerializeFormSections, which appends the payload into a
            // List<byte> one byte at a time: a 430 MB demo never finishes, which is why this client
            // could not analyze a realistic demo before. Here the file is read straight into its final
            // offset, so the upload costs one pass over the data and no second copy.
            byte[] body;
            try
            {
                body = new byte[(int)total];
            }
            catch (OutOfMemoryException)
            {
                SetState(DemoAnalysisState.Failed, Copy.demoStatusOutOfMemory);
                active = null;
                yield break;
            }

            Buffer.BlockCopy(headerBytes, 0, body, 0, headerBytes.Length);

            var offset = headerBytes.Length;
            var failure = string.Empty;

            using (var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ReadChunkBytes,
                FileOptions.SequentialScan))
            {
                var remaining = file.Length;
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        read = stream.Read(
                            body,
                            offset,
                            (int)Math.Min(ReadChunkBytes, remaining));
                    }
                    catch (Exception exception)
                    {
                        failure = Trim(exception.Message);
                        break;
                    }

                    if (read <= 0)
                    {
                        failure = Copy.demoStatusReadTruncated;
                        break;
                    }

                    offset += read;
                    remaining -= read;

                    // Yielding per chunk keeps the status line repainting instead of freezing the
                    // whole client for the length of the read.
                    yield return null;
                }
            }

            if (!string.IsNullOrEmpty(failure))
            {
                SetState(DemoAnalysisState.Failed, failure);
                active = null;
                yield break;
            }

            Buffer.BlockCopy(footerBytes, 0, body, offset, footerBytes.Length);

            SetState(
                DemoAnalysisState.Requesting,
                string.Format(Copy.demoStatusAnalyzing, file.Name));

            using (var request = new UnityWebRequest(Copy.demoEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "multipart/form-data; boundary=" + boundary);
                request.timeout = Copy.demoTimeoutSeconds;

                yield return request.SendWebRequest();
                Apply(request);
            }

            active = null;
        }

        private IEnumerator GetSample()
        {
            SetState(DemoAnalysisState.Requesting, Copy.demoStatusSample);

            using (var request = UnityWebRequest.Get(Copy.demoEndpoint + "/sample"))
            {
                request.timeout = Copy.demoSampleTimeoutSeconds;
                yield return request.SendWebRequest();
                Apply(request);
            }

            active = null;
        }

        private void Apply(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetState(DemoAnalysisState.Failed, DescribeFailure(request));
                return;
            }

            DemoReport parsed;
            try
            {
                parsed = JsonUtility.FromJson<DemoReport>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                SetState(DemoAnalysisState.Failed, Trim(exception.Message));
                return;
            }

            if (parsed == null || parsed.player == null)
            {
                SetState(DemoAnalysisState.Failed, Copy.demoStatusInvalid);
                return;
            }

            Report = parsed;
            SetState(
                DemoAnalysisState.Ready,
                string.Format(
                    Copy.demoStatusReady,
                    Describe(parsed.map_name),
                    parsed.rounds));

            ReportLoaded?.Invoke(parsed);
        }

        /// <summary>
        /// Prefers the service's own message over Unity's transport error, because FastAPI returns the
        /// actionable detail (unparsable demo, no competitive rounds, player not found) in the body.
        /// </summary>
        private string DescribeFailure(UnityWebRequest request)
        {
            if (request.responseCode == 0)
                return Copy.demoStatusOffline;

            var body = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            var detail = ExtractDetail(body);
            return string.IsNullOrEmpty(detail)
                ? Trim(request.error)
                : Trim(detail);
        }

        /// <summary>
        /// Pulls the string out of FastAPI's <c>{"detail": "..."}</c> envelope. Hand-written because
        /// JsonUtility cannot deserialize into a bare string field without a wrapper type, and the
        /// error body is the only JSON shape this class reads besides the report itself.
        /// </summary>
        private static string ExtractDetail(string body)
        {
            if (string.IsNullOrEmpty(body))
                return string.Empty;

            const string key = "\"detail\"";
            var keyIndex = body.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
                return string.Empty;

            var open = body.IndexOf('"', keyIndex + key.Length + 1);
            if (open < 0)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            for (var index = open + 1; index < body.Length; index++)
            {
                var character = body[index];
                if (character == '\\' && index + 1 < body.Length)
                {
                    builder.Append(body[index + 1]);
                    index++;
                    continue;
                }

                if (character == '"')
                    break;

                builder.Append(character);
            }

            return builder.ToString();
        }

        // ------------------------------------------------------------------ helpers

        private WarRoomTheme.Content Copy =>
            theme != null ? theme.Data : FallbackCopy;

        private static WarRoomTheme.Content fallbackCopy;

        /// <summary>
        /// Keeps the controller usable when it is added by hand without a theme, so a missing asset
        /// reference surfaces as default copy rather than a null reference during a request.
        /// </summary>
        private static WarRoomTheme.Content FallbackCopy =>
            fallbackCopy ??= new WarRoomTheme.Content();

        private void SetState(DemoAnalysisState state, string message)
        {
            State = state;
            StatusMessage = message ?? string.Empty;
            StateChanged?.Invoke();
        }

        private static string Describe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.ToUpperInvariant();
        }

        private static float Megabytes(long bytes)
        {
            return bytes / (1024f * 1024f);
        }

        /// <summary>Status labels are a single line on a narrow rail, so long detail is truncated.</summary>
        private static string Trim(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "REQUEST FAILED";

            var flat = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length <= 78 ? flat : flat.Substring(0, 77) + "…";
        }
    }
}
