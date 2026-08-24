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

        // Control deck
        public StudioHudController.DeckButton ImportButton;
        public StudioHudController.DeckButton PlayButton;
        public StudioHudController.DeckButton LiveButton;
    }
}
