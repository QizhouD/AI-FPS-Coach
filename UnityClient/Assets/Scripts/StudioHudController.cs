using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FpsAiCoach
{
    /// <summary>
    /// Orchestrates the war-room HUD: wires the deck buttons, mirrors
    /// <see cref="TacticalScreenController"/> state onto the 2D labels, and drives the timeline and
    /// reticle. Holds no layout numbers or copy of its own; everything comes from
    /// <see cref="WarRoomTheme"/> so the builder and runtime cannot drift apart.
    /// </summary>
    public sealed class StudioHudController : MonoBehaviour
    {
        [Serializable]
        public sealed class DeckButton
        {
            public Button button;
            public Image fill;
            public Image border;
            public TMP_Text label;
        }

        /// Matches MIN_REACTION_SAMPLES in the service's shot detector.
        private const int MinReactionSamples = 3;

        [Header("Configuration")]
        [SerializeField] private WarRoomTheme theme;

        [Header("Modules")]
        [SerializeField] private TacticalScreenController screen;
        [SerializeField] private TimelineController timeline;
        [SerializeField] private StudioAnimator studioAnimator;
        [SerializeField] private MatchLibraryController library;
        [SerializeField] private InsightsController insights;
        [SerializeField] private DemoAnalysisController demoAnalysis;
        [SerializeField] private ClipRecorder recorder;
        [SerializeField] private VisionInferenceOverlay visionOverlay;

        [Header("Deck buttons")]
        [SerializeField] private DeckButton importButton = new DeckButton();
        [SerializeField] private DeckButton playButton = new DeckButton();
        [SerializeField] private DeckButton liveButton = new DeckButton();
        [SerializeField] private DeckButton recordButton = new DeckButton();
        [SerializeField] private DeckButton saveClipButton = new DeckButton();

        [Header("Library footer buttons")]
        [SerializeField] private DeckButton importDemoButton = new DeckButton();
        [SerializeField] private DeckButton sampleButton = new DeckButton();

        [Header("Labels")]
        [SerializeField] private TMP_Text screenStatusLabel;
        [SerializeField] private TMP_Text timecodeLabel;
        [SerializeField] private TMP_Text headerModeLabel;
        [SerializeField] private TMP_Text headerMatchLabel;
        [SerializeField] private TMP_Text demoStatusLabel;
        [SerializeField] private TMP_Text captureStatusLabel;

        private int lastImportFrame = -1;
        private int lastDemoFrame = -1;
        private bool liveRequested;

        public void Configure(
            WarRoomTheme configuredTheme,
            TacticalScreenController configuredScreen,
            TimelineController configuredTimeline,
            StudioAnimator configuredAnimator,
            MatchLibraryController configuredLibrary,
            InsightsController configuredInsights,
            DemoAnalysisController configuredDemoAnalysis,
            ClipRecorder configuredRecorder,
            VisionInferenceOverlay configuredVisionOverlay = null)
        {
            theme = configuredTheme;
            screen = configuredScreen;
            timeline = configuredTimeline;
            studioAnimator = configuredAnimator;
            library = configuredLibrary;
            insights = configuredInsights;
            demoAnalysis = configuredDemoAnalysis;
            recorder = configuredRecorder;
            visionOverlay = configuredVisionOverlay;
        }

        public void BindButtons(DeckButton import, DeckButton play, DeckButton live)
        {
            importButton = import;
            playButton = play;
            liveButton = live;
        }

        public void BindCapture(DeckButton record, DeckButton saveClip, TMP_Text status)
        {
            recordButton = record;
            saveClipButton = saveClip;
            captureStatusLabel = status;
        }

        public void BindLibraryFooter(DeckButton importDemo, DeckButton sample, TMP_Text status)
        {
            importDemoButton = importDemo;
            sampleButton = sample;
            demoStatusLabel = status;
        }

        public void BindLabels(TMP_Text status, TMP_Text timecode, TMP_Text mode, TMP_Text match)
        {
            screenStatusLabel = status;
            timecodeLabel = timecode;
            headerModeLabel = mode;
            headerMatchLabel = match;
        }

        /// <summary>
        /// Wiring lives on enable rather than in Start so that it is restored after a domain reload, which
        /// any script edit during play triggers. Neither runtime <c>AddListener</c> calls nor C# delegates
        /// survive one, and Start never runs again, so the whole HUD used to come back inert: buttons dead,
        /// status lines frozen on whatever they last displayed.
        ///
        /// Everything below is either a subscription undone in <see cref="OnDisable"/> or an idempotent
        /// state sync, so repeating it on each enable is harmless. The trailing syncs also make this
        /// independent of initialisation order: whichever controller comes up second announces itself
        /// through its own event, and the subscription is already in place to hear it.
        /// </summary>
        private void OnEnable()
        {
            // A scene saved before the overlay existed leaves this reference empty, and the whole
            // aim rail then stays on its placeholder text with nothing logged. The overlay is a
            // sibling component, so recover it rather than fail silently.
            if (visionOverlay == null)
            {
                visionOverlay = GetComponent<VisionInferenceOverlay>();
                if (visionOverlay == null)
                    Debug.LogWarning("StudioHudController has no VisionInferenceOverlay; " +
                                     "the aim metrics rail will not update.", this);
            }

            if (importButton?.button != null)
                importButton.button.onClick.AddListener(HandleImportClicked);
            if (playButton?.button != null)
                playButton.button.onClick.AddListener(HandlePlayClicked);
            if (liveButton?.button != null)
                liveButton.button.onClick.AddListener(HandleLiveClicked);
            if (importDemoButton?.button != null)
                importDemoButton.button.onClick.AddListener(HandleImportDemoClicked);
            if (sampleButton?.button != null)
                sampleButton.button.onClick.AddListener(HandleSampleClicked);
            if (recordButton?.button != null)
                recordButton.button.onClick.AddListener(HandleRecordClicked);
            if (saveClipButton?.button != null)
                saveClipButton.button.onClick.AddListener(HandleSaveClipClicked);

            if (screen != null)
                screen.StateChanged += HandleScreenStateChanged;

            if (demoAnalysis != null)
            {
                demoAnalysis.StateChanged += HandleDemoStateChanged;
                demoAnalysis.ReportLoaded += HandleReportLoaded;
            }

            if (recorder != null)
                recorder.StateChanged += HandleCaptureStateChanged;

            if (visionOverlay != null)
            {
                visionOverlay.MetricsReady += HandleAimMetricsReady;
                // A domain reload can land after a job has already finished, so replay the last
                // result instead of leaving the rail on whatever it was showing before.
                if (visionOverlay.LatestMetrics != null)
                    HandleAimMetricsReady(visionOverlay.LatestMetrics);
            }

            if (library != null)
            {
                library.SelectionChanged += HandleMatchSelected;
                HandleMatchSelected(library.SelectedIndex);
            }

            ApplyInsightContent();
            HandleScreenStateChanged();
            HandleDemoStateChanged();
            HandleCaptureStateChanged();
            timeline?.SetProgress(0f);
        }

        private void OnDisable()
        {
            if (importButton?.button != null)
                importButton.button.onClick.RemoveListener(HandleImportClicked);
            if (playButton?.button != null)
                playButton.button.onClick.RemoveListener(HandlePlayClicked);
            if (liveButton?.button != null)
                liveButton.button.onClick.RemoveListener(HandleLiveClicked);
            if (importDemoButton?.button != null)
                importDemoButton.button.onClick.RemoveListener(HandleImportDemoClicked);
            if (sampleButton?.button != null)
                sampleButton.button.onClick.RemoveListener(HandleSampleClicked);
            if (recordButton?.button != null)
                recordButton.button.onClick.RemoveListener(HandleRecordClicked);
            if (saveClipButton?.button != null)
                saveClipButton.button.onClick.RemoveListener(HandleSaveClipClicked);

            if (screen != null)
                screen.StateChanged -= HandleScreenStateChanged;

            if (demoAnalysis != null)
            {
                demoAnalysis.StateChanged -= HandleDemoStateChanged;
                demoAnalysis.ReportLoaded -= HandleReportLoaded;
            }

            if (recorder != null)
                recorder.StateChanged -= HandleCaptureStateChanged;

            if (visionOverlay != null)
                visionOverlay.MetricsReady -= HandleAimMetricsReady;

            if (library != null)
                library.SelectionChanged -= HandleMatchSelected;
        }

        private void Update()
        {
            if (screen == null)
                return;

            if (screen.IsLiveMode)
            {
                SetTimecode(theme != null ? theme.Data.modeLive : "LIVE");
                return;
            }

            if (!screen.IsVideoReady)
                return;

            timeline?.SetProgress(screen.NormalizedProgress);
            SetTimecode($"{FormatTime(screen.CurrentTime)}  //  {FormatTime(screen.Duration)}");
            UpdatePlayLabel();
        }

        // ------------------------------------------------------------------ button handlers

        private void HandleImportClicked()
        {
            // The world-space ray interactor can deliver a click and an OnGUI click in one frame.
            if (lastImportFrame == Time.frameCount)
                return;
            lastImportFrame = Time.frameCount;

            screen?.SelectAndLoadVideo();
        }

        private void HandlePlayClicked()
        {
            if (screen == null)
                return;

            screen.TogglePlayback();
            UpdatePlayLabel();
        }

        private void HandleLiveClicked()
        {
            if (screen == null || theme == null)
                return;

            var app = CoachLiveApp.Instance;

            if (liveRequested)
            {
                liveRequested = false;
                app?.StopLiveSource();
                screen.ExitLiveMode();
                SetLabel(liveButton?.label, theme.Data.buttonLive);
                return;
            }

            if (app == null || !app.TryStartLiveSource())
            {
                SetLabel(screenStatusLabel, theme.Data.statusLiveUnavailable);
                return;
            }

            liveRequested = true;
            screen.EnterLiveMode(app.LiveTexture, app.LiveDeviceName);
            SetLabel(liveButton?.label, theme.Data.buttonDemo);
        }

        private void HandleImportDemoClicked()
        {
            // Same guard as the video import: a world-space ray click and a UI click can both land in
            // one frame, which would otherwise open two file dialogs.
            if (lastDemoFrame == Time.frameCount)
                return;
            lastDemoFrame = Time.frameCount;

            demoAnalysis?.SelectAndAnalyze();
        }

        private void HandleSampleClicked()
        {
            demoAnalysis?.LoadSample();
        }

        private void HandleRecordClicked()
        {
            recorder?.ToggleTake();
        }

        private void HandleSaveClipClicked()
        {
            recorder?.SaveClip();
        }

        private void HandleMatchSelected(int index)
        {
            if (headerMatchLabel == null || library == null)
                return;

            var map = library.SelectedMapName();
            headerMatchLabel.text = string.IsNullOrEmpty(map) ? string.Empty : map;
        }

        // ------------------------------------------------------------------ demo analysis

        private void HandleDemoStateChanged()
        {
            if (demoAnalysis == null)
                return;

            SetLabel(demoStatusLabel, demoAnalysis.StatusMessage);

            if (demoStatusLabel != null && theme != null)
            {
                var palette = theme.Colors;
                demoStatusLabel.color = demoAnalysis.State switch
                {
                    DemoAnalysisState.Ready => WarRoomColor.ForUi(palette.cyanPrimary),
                    DemoAnalysisState.Failed => WarRoomColor.ForUi(palette.amberAlert),
                    DemoAnalysisState.Rejected => WarRoomColor.ForUi(palette.amberAlert),
                    _ => WarRoomColor.ForUi(palette.textMuted)
                };
            }

            var idle = !demoAnalysis.IsBusy;
            SetInteractable(importDemoButton, idle);
            SetInteractable(sampleButton, idle);
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Mirrors the recorder onto the chrome. RECORD doubles as STOP once a take is running, so the
        /// deck does not need a sixth button for an action that only exists in one state.
        /// </summary>
        private void HandleCaptureStateChanged()
        {
            if (recorder == null || theme == null)
                return;

            // A player build has no encoder, so the deck drops the two controls instead of showing
            // buttons that cannot act. OBS is the recording path outside the editor.
            if (!ClipRecorder.IsAvailable)
            {
                SetVisible(recordButton, false);
                SetVisible(saveClipButton, false);
                SetLabel(captureStatusLabel, theme.Data.captureStatusUseObs);
                return;
            }

            SetLabel(captureStatusLabel, recorder.StatusMessage);

            if (captureStatusLabel != null)
            {
                var palette = theme.Colors;
                captureStatusLabel.color = recorder.State switch
                {
                    ClipRecorderState.Recording => WarRoomColor.ForUi(palette.amberAlert),
                    ClipRecorderState.Saved => WarRoomColor.ForUi(palette.cyanPrimary),
                    ClipRecorderState.Failed => WarRoomColor.ForUi(palette.amberAlert),
                    _ => WarRoomColor.ForUi(palette.textMuted)
                };
            }

            SetLabel(
                recordButton?.label,
                recorder.IsRecordingTake
                    ? theme.Data.buttonRecordStop
                    : theme.Data.buttonRecord);

            // Muxing owns the encoder, so neither action may start until it finishes. Saving a clip
            // additionally needs a live buffer, which a running take deliberately does not keep.
            SetInteractable(recordButton, !recorder.IsBusy);
            SetInteractable(saveClipButton, recorder.CanSaveClip);
        }

        /// <summary>
        /// Moves a report onto the rails: the analyzed match takes the most-recent library slot, the
        /// three metric bars take the headline numbers, and the cards take the service's insights.
        /// </summary>
        private void HandleReportLoaded(DemoReport report)
        {
            if (report == null || theme == null)
                return;

            var data = theme.Data;
            var stats = report.player ?? new DemoPlayerStats();

            if (library != null && library.RowCount > 0)
            {
                library.SetRow(
                    0,
                    FormatMapName(report.map_name),
                    $"{stats.kills} : {stats.deaths}",
                    $"{Upper(stats.name)}  ·  {report.rounds} ROUNDS");
                library.ForceSelect(0);
            }

            if (insights != null)
            {
                // A finished practice review renames these bars, so restore the demo labels before
                // writing demo numbers into them.
                var authored = data.metrics;
                for (var index = 0; index < authored.Length && index < insights.MetricCount; index++)
                    insights.SetMetricLabel(index, authored[index].label);

                // Bars are normalized against "excellent" ceilings from the theme, while the readout
                // keeps the real figure: a K/D of 1.46 fills 73% of the bar but still prints as 1.46.
                insights.SetMetric(
                    0,
                    stats.kd_ratio / Mathf.Max(0.01f, data.metricKdCeiling),
                    stats.kd_ratio.ToString("0.00"));

                insights.SetMetric(
                    1,
                    stats.headshot_percentage / 100f,
                    Mathf.RoundToInt(stats.headshot_percentage).ToString());

                insights.SetMetric(
                    2,
                    stats.adr / Mathf.Max(0.01f, data.metricAdrCeiling),
                    Mathf.RoundToInt(stats.adr).ToString());

                ApplyReportCards(report);
            }
        }

        private void ApplyReportCards(DemoReport report)
        {
            var priority = WarRoomColor.ForUi(theme.Colors.amberAlert);
            var normal = WarRoomColor.ForUi(theme.Colors.blueElectric);
            var reported = report.insights ?? Array.Empty<DemoInsight>();
            var cards = theme.Data.insights.Length;

            for (var index = 0; index < cards; index++)
            {
                if (index >= reported.Length)
                {
                    // Stale placeholder copy next to a real report would read as analysis output, so
                    // unused cards are emptied rather than left as authored.
                    insights.SetCard(index, string.Empty, string.Empty, false, priority, normal);
                    continue;
                }

                var entry = reported[index];
                insights.SetCard(
                    index,
                    Upper(entry.title),
                    entry.evidence,
                    IsUrgent(entry.severity),
                    priority,
                    normal);
            }
        }

        // ------------------------------------------------------------------ aim analysis

        /// <summary>
        /// Moves a finished practice review onto the right rail.
        ///
        /// The rail is shared with the demo report rather than duplicated: only one of the two
        /// analyses is on screen at a time, and the bars are relabelled so a deviation in degrees
        /// is never read as a K/D.
        /// </summary>
        private void HandleAimMetricsReady(VisionSessionMetrics metrics)
        {
            if (metrics == null || theme == null || insights == null)
                return;

            var data = theme.Data;
            var deviation = metrics.placement_deviation ?? new VisionDeviationStats();
            var vertical = metrics.vertical_bias ?? new VisionBiasStats();
            var tracking = metrics.effective_tracking ?? new VisionTrackingStats();

            insights.SetMetricLabel(0, data.metricLabelAimDeviation);
            insights.SetMetricLabel(1, data.metricLabelOnTarget);
            insights.SetMetricLabel(2, data.metricLabelVerticalBias);

            // Less deviation is better, so these bars fill as the error shrinks.
            insights.SetMetric(
                0,
                1f - Mathf.Clamp01(
                    deviation.mean_deg / Mathf.Max(0.01f, data.aimDeviationCeilingDeg)),
                deviation.mean_deg.ToString("0.0"));

            insights.SetMetric(
                1,
                tracking.on_target_ratio,
                Mathf.RoundToInt(tracking.on_target_ratio * 100f).ToString());

            insights.SetMetric(
                2,
                1f - Mathf.Clamp01(
                    Mathf.Abs(vertical.mean_deg) / Mathf.Max(0.01f, data.aimBiasCeilingDeg)),
                vertical.mean_deg.ToString("+0.0;-0.0;0.0"));

            ApplyAimCards(metrics);
        }

        private void ApplyAimCards(VisionSessionMetrics metrics)
        {
            var data = theme.Data;
            var priority = WarRoomColor.ForUi(theme.Colors.amberAlert);
            var normal = WarRoomColor.ForUi(theme.Colors.blueElectric);
            var vertical = metrics.vertical_bias ?? new VisionBiasStats();
            var shots = metrics.shots ?? new VisionShotStats();

            insights.SetCard(
                0,
                data.aimCardPlacementTitle,
                metrics.headline,
                metrics.placement_deviation != null &&
                metrics.placement_deviation.mean_deg > data.aimDeviationCeilingDeg * 0.5f,
                priority,
                normal);

            insights.SetCard(
                1,
                data.aimCardVerticalTitle,
                DescribeVerticalBias(vertical),
                !string.Equals(vertical.direction, "neutral", StringComparison.Ordinal),
                priority,
                normal);

            insights.SetCard(
                2,
                data.aimCardShotsTitle,
                DescribeShots(shots, data.aimCardShotsUnavailable),
                shots.overcorrection_ratio > 0.3f,
                priority,
                normal);
        }

        private static string DescribeVerticalBias(VisionBiasStats vertical)
        {
            if (vertical.count == 0)
                return "No head was detected, so no vertical tendency could be measured.";

            switch (vertical.direction)
            {
                case "aims_low":
                    return
                        $"Crosshair sits {Mathf.Abs(vertical.mean_deg):0.0}° below head level in " +
                        $"{vertical.positive_ratio:P0} of frames. Raise your resting height.";
                case "aims_high":
                    return
                        $"Crosshair sits {Mathf.Abs(vertical.mean_deg):0.0}° above head level. " +
                        "Lower your resting height slightly.";
                default:
                    return
                        $"No systematic vertical lean; spread is {vertical.std_deg:0.0}°.";
            }
        }

        private static string DescribeShots(VisionShotStats shots, string unavailable)
        {
            if (shots.detected_shots == 0)
                return unavailable;

            var text =
                $"{shots.detected_shots} shots, {shots.aligned_shots} measured. " +
                $"Mean deviation at the moment of firing {shots.deviation.mean_deg:0.0}°.";

            // On a range map the bots never leave the screen, so the whole clip is one
            // engagement and the "mean" is a single sample. Say so instead of quoting it.
            if (shots.mean_reaction_seconds > 0f)
            {
                text += shots.reaction_samples >= MinReactionSamples
                    ? $" Mean reaction {shots.mean_reaction_seconds * 1000f:0} ms" +
                      $" over {shots.reaction_samples} engagements."
                    : $" Reaction time rests on {shots.reaction_samples} engagement" +
                      (shots.reaction_samples == 1 ? "" : "s") + " and is not an average.";
            }
            if (shots.overcorrection_count > 0)
                text += $" Overcorrected before {shots.overcorrection_ratio:P0} of shots.";
            return text;
        }

        private static bool IsUrgent(string severity)
        {
            if (string.IsNullOrEmpty(severity))
                return false;

            return severity.Equals("warning", StringComparison.OrdinalIgnoreCase) ||
                   severity.Equals("danger", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Turns a service map id into rail copy: <c>de_mirage</c> reads as <c>MIRAGE</c>, matching how
        /// the seed rows are authored.
        /// </summary>
        private static string FormatMapName(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
                return "UNKNOWN";

            var trimmed = mapName.Trim();
            foreach (var prefix in MapPrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(prefix.Length);
                    break;
                }
            }

            return Upper(trimmed);
        }

        private static readonly string[] MapPrefixes = { "de_", "cs_", "ar_" };

        private static string Upper(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        // ------------------------------------------------------------------ state mirroring

        private void HandleScreenStateChanged()
        {
            if (screen == null || theme == null)
                return;

            SetLabel(screenStatusLabel, DescribeState());
            SetLabel(
                headerModeLabel,
                screen.IsLiveMode ? theme.Data.modeLive : theme.Data.modeDemo);

            var hasContent =
                screen.State == TacticalScreenState.Ready ||
                screen.State == TacticalScreenState.Live;

            studioAnimator?.SetReticleVisible(!hasContent);

            var playable = screen.IsVideoReady && !screen.IsLiveMode;
            SetInteractable(playButton, playable);
            UpdatePlayLabel();

            if (!screen.IsVideoReady && !screen.IsLiveMode)
            {
                timeline?.SetProgress(0f);
                SetTimecode($"{FormatTime(0d)}  //  {FormatTime(0d)}");
            }
        }

        private string DescribeState()
        {
            var data = theme.Data;
            switch (screen.State)
            {
                case TacticalScreenState.Selecting:
                    return data.statusSelecting;
                case TacticalScreenState.Loading:
                    return data.statusLoading;
                case TacticalScreenState.Ready:
                    return Join(data.statusReadyPrefix, screen.SourceLabel);
                case TacticalScreenState.Error:
                    return data.statusError;
                case TacticalScreenState.Unsupported:
                    return data.statusUnsupported;
                case TacticalScreenState.Missing:
                    return data.statusMissing;
                case TacticalScreenState.Live:
                    return Join(data.statusLive, screen.SourceLabel);
                default:
                    return data.statusNoVideo;
            }
        }

        private void ApplyInsightContent()
        {
            if (insights == null || theme == null)
                return;

            var metrics = theme.Data.metrics;
            for (var index = 0; index < metrics.Length && index < insights.MetricCount; index++)
                insights.SetMetric(index, metrics[index].value, metrics[index].display);

            var cards = theme.Data.insights;
            var priority = WarRoomColor.ForUi(theme.Colors.amberAlert);
            var normal = WarRoomColor.ForUi(theme.Colors.blueElectric);
            for (var index = 0; index < cards.Length; index++)
            {
                insights.SetCard(
                    index,
                    cards[index].title,
                    cards[index].body,
                    cards[index].highPriority,
                    priority,
                    normal);
            }
        }

        // ------------------------------------------------------------------ small helpers

        private void UpdatePlayLabel()
        {
            if (playButton?.label == null || theme == null)
                return;

            playButton.label.text = screen != null && screen.IsPlaying
                ? theme.Data.buttonPause
                : theme.Data.buttonPlay;
        }

        private static void SetVisible(DeckButton target, bool visible)
        {
            if (target?.button != null)
                target.button.gameObject.SetActive(visible);
        }

        private static void SetInteractable(DeckButton target, bool interactable)
        {
            if (target?.button == null)
                return;

            target.button.interactable = interactable;

            if (target.label != null)
            {
                var color = target.label.color;
                target.label.color = new Color(color.r, color.g, color.b, interactable ? 1f : 0.35f);
            }

            if (target.border != null)
            {
                var color = target.border.color;
                target.border.color = new Color(color.r, color.g, color.b, interactable ? 1f : 0.3f);
            }
        }

        private static void SetLabel(TMP_Text label, string value)
        {
            if (label != null)
                label.text = value;
        }

        private void SetTimecode(string value)
        {
            SetLabel(timecodeLabel, value);
        }

        private static string Join(string prefix, string detail)
        {
            return string.IsNullOrEmpty(detail) ? prefix : prefix + "  ·  " + detail;
        }

        private static string FormatTime(double seconds)
        {
            var total = Math.Max(0, (int)Math.Floor(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
