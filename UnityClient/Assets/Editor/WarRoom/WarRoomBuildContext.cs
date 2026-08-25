using TMPro;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    /// <summary>
    /// Carries the references each build stage produces so the final wiring step can connect the
    /// runtime controllers with serialized references instead of name lookups.
    /// </summary>
    internal sealed class WarRoomBuildContext
    {
        public WarRoomTheme Theme;
        public WarRoomMaterials Materials;
        public WarRoomCanvasKit Ui;

        public GameObject Root;
        public Camera Camera;

        // Tactical display
        public Renderer ScreenSurface;
        public Transform Reticle;
        public Transform ReplayMarkers;
        public Transform TimelineProgress;
        public Transform TimelineEvents;
        public TimelineController Timeline;
        public TMP_Text ScreenStatusLabel;
        public TMP_Text TimecodeLabel;

        // Header
        public Transform Beacon;
        public TMP_Text HeaderModeLabel;
        public TMP_Text HeaderMatchLabel;

        // Rails
        public MatchLibraryController Library;
        public InsightsController Insights;

        // Match library footer (demo analysis entry point)
        public StudioHudController.DeckButton ImportDemoButton;
        public StudioHudController.DeckButton SampleButton;
        public TMP_Text DemoStatusLabel;

        // Control deck
        public StudioHudController.DeckButton ImportButton;
        public StudioHudController.DeckButton PlayButton;
        public StudioHudController.DeckButton LiveButton;

        // Control deck, capture group
        public StudioHudController.DeckButton RecordButton;
        public StudioHudController.DeckButton SaveClipButton;

        /// <summary>Lives on the screen-space chrome rather than the deck, which has no room for it.</summary>
        public TMP_Text CaptureStatusLabel;
    }
}
