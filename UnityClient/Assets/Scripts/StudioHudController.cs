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

        [Header("Configuration")]
        [SerializeField] private WarRoomTheme theme;

        [Header("Modules")]
        [SerializeField] private TacticalScreenController screen;
        [SerializeField] private TimelineController timeline;
        [SerializeField] private StudioAnimator studioAnimator;
        [SerializeField] private MatchLibraryController library;
        [SerializeField] private InsightsController insights;

        [Header("Deck buttons")]
        [SerializeField] private DeckButton importButton = new DeckButton();
        [SerializeField] private DeckButton playButton = new DeckButton();
        [SerializeField] private DeckButton liveButton = new DeckButton();

        [Header("Labels")]
        [SerializeField] private TMP_Text screenStatusLabel;
        [SerializeField] private TMP_Text timecodeLabel;
        [SerializeField] private TMP_Text headerModeLabel;
        [SerializeField] private TMP_Text headerMatchLabel;

        private int lastImportFrame = -1;
        private bool liveRequested;

        public void Configure(
            WarRoomTheme configuredTheme,
            TacticalScreenController configuredScreen,
            TimelineController configuredTimeline,
            StudioAnimator configuredAnimator,
            MatchLibraryController configuredLibrary,
            InsightsController configuredInsights)
        {
            theme = configuredTheme;
            screen = configuredScreen;
            timeline = configuredTimeline;
            studioAnimator = configuredAnimator;
            library = configuredLibrary;
            insights = configuredInsights;
        }

        public void BindButtons(DeckButton import, DeckButton play, DeckButton live)
        {
            importButton = import;
            playButton = play;
            liveButton = live;
        }

        public void BindLabels(TMP_Text status, TMP_Text timecode, TMP_Text mode, TMP_Text match)
        {
            screenStatusLabel = status;
            timecodeLabel = timecode;
            headerModeLabel = mode;
            headerMatchLabel = match;
        }

        private void Start()
        {
            if (importButton?.button != null)
                importButton.button.onClick.AddListener(HandleImportClicked);
            if (playButton?.button != null)
                playButton.button.onClick.AddListener(HandlePlayClicked);
            if (liveButton?.button != null)
                liveButton.button.onClick.AddListener(HandleLiveClicked);

            if (screen != null)
                screen.StateChanged += HandleScreenStateChanged;

            if (library != null)
            {
                library.SelectionChanged += HandleMatchSelected;
                HandleMatchSelected(library.SelectedIndex);
            }

            ApplyInsightContent();
            HandleScreenStateChanged();
            timeline?.SetProgress(0f);
        }

        private void OnDestroy()
        {
            if (importButton?.button != null)
                importButton.button.onClick.RemoveListener(HandleImportClicked);
            if (playButton?.button != null)
                playButton.button.onClick.RemoveListener(HandlePlayClicked);
            if (liveButton?.button != null)
                liveButton.button.onClick.RemoveListener(HandleLiveClicked);

            if (screen != null)
                screen.StateChanged -= HandleScreenStateChanged;
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

        private void HandleMatchSelected(int index)
        {
            if (headerMatchLabel == null || library == null)
                return;

            var map = library.SelectedMapName();
            headerMatchLabel.text = string.IsNullOrEmpty(map) ? string.Empty : map;
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
                insights.SetMetric(index, metrics[index].value);

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
