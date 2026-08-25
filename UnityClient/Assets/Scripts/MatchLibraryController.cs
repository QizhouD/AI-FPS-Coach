using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FpsAiCoach
{
    /// <summary>
    /// Fixed-length match list rendered on a single world-space canvas. Selection is expressed the
    /// way the war-room language demands: a hairline border plus a left indicator bar, never a
    /// filled highlight.
    /// </summary>
    public sealed class MatchLibraryController : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public Button button;
            public Image background;
            public Image border;
            public Image indicator;
            public TMP_Text mapLabel;
            public TMP_Text scoreLabel;
            public TMP_Text metaLabel;
        }

        [SerializeField] private Row[] rows = Array.Empty<Row>();

        [Header("Idle state")]
        [SerializeField] private Color idleBackground = new Color(0.055f, 0.078f, 0.11f, 0.55f);
        [SerializeField] private Color idleBorder = new Color(0.106f, 0.153f, 0.2f, 0.9f);
        [SerializeField] private Color idleIndicator = new Color(0.35f, 0.41f, 0.47f, 0.35f);
        [SerializeField] private Color idleMapText = new Color(0.784f, 0.839f, 0.878f, 1f);
        [SerializeField] private Color idleScoreText = new Color(0.561f, 0.639f, 0.702f, 1f);

        [Header("Selected state")]
        [SerializeField] private Color selectedBackground = new Color(0.075f, 0.11f, 0.149f, 0.9f);
        [SerializeField] private Color selectedBorder = new Color(0f, 0.898f, 1f, 0.9f);
        [SerializeField] private Color selectedIndicator = new Color(0f, 0.898f, 1f, 1f);
        [SerializeField] private Color selectedMapText = Color.white;
        [SerializeField] private Color selectedScoreText = new Color(0f, 0.898f, 1f, 1f);

        private int selectedIndex;

        /// <summary>Raised with the newly selected row index.</summary>
        public event Action<int> SelectionChanged;

        public int SelectedIndex => selectedIndex;
        public int RowCount => rows != null ? rows.Length : 0;

        public void Configure(Row[] configuredRows)
        {
            rows = configuredRows ?? Array.Empty<Row>();
        }

        public void ApplyPalette(
            Color rowIdleBackground,
            Color rowIdleBorder,
            Color rowIdleIndicator,
            Color rowIdleMapText,
            Color rowIdleScoreText,
            Color rowSelectedBackground,
            Color rowSelectedBorder,
            Color rowSelectedIndicator,
            Color rowSelectedMapText,
            Color rowSelectedScoreText)
        {
            idleBackground = rowIdleBackground;
            idleBorder = rowIdleBorder;
            idleIndicator = rowIdleIndicator;
            idleMapText = rowIdleMapText;
            idleScoreText = rowIdleScoreText;
            selectedBackground = rowSelectedBackground;
            selectedBorder = rowSelectedBorder;
            selectedIndicator = rowSelectedIndicator;
            selectedMapText = rowSelectedMapText;
            selectedScoreText = rowSelectedScoreText;
        }

        /// <summary>
        /// On enable rather than in Start so the row listeners come back after a domain reload, which drops
        /// them and never calls Start again. <see cref="OnDisable"/> clears them, so the pairing also keeps
        /// a second enable from stacking a duplicate listener on every row.
        /// </summary>
        private void OnEnable()
        {
            for (var index = 0; index < rows.Length; index++)
            {
                var captured = index;
                if (rows[index].button != null)
                    rows[index].button.onClick.AddListener(() => Select(captured));
            }

            Refresh();
        }

        private void OnDisable()
        {
            foreach (var row in rows)
            {
                if (row != null && row.button != null)
                    row.button.onClick.RemoveAllListeners();
            }
        }

        public void Select(int index)
        {
            if (rows.Length == 0)
                return;

            var clamped = Mathf.Clamp(index, 0, rows.Length - 1);
            if (clamped == selectedIndex)
                return;

            selectedIndex = clamped;
            Refresh();
            SelectionChanged?.Invoke(selectedIndex);
        }

        /// <summary>Reapplies selection visuals. Safe to call after the palette changes.</summary>
        public void Refresh()
        {
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row == null)
                    continue;

                var isSelected = index == selectedIndex;

                if (row.background != null)
                    row.background.color = isSelected ? selectedBackground : idleBackground;
                if (row.border != null)
                    row.border.color = isSelected ? selectedBorder : idleBorder;
                if (row.indicator != null)
                    row.indicator.color = isSelected ? selectedIndicator : idleIndicator;
                if (row.mapLabel != null)
                    row.mapLabel.color = isSelected ? selectedMapText : idleMapText;
                if (row.scoreLabel != null)
                    row.scoreLabel.color = isSelected ? selectedScoreText : idleScoreText;
            }
        }

        /// <summary>
        /// Overwrites a row's text. Used when an analyzed report takes the most-recent slot, so the
        /// rail shows the match actually being reviewed instead of seed content.
        /// </summary>
        public void SetRow(int index, string map, string score, string meta)
        {
            if (rows == null || index < 0 || index >= rows.Length)
                return;

            var row = rows[index];
            if (row == null)
                return;

            if (row.mapLabel != null)
                row.mapLabel.text = map;
            if (row.scoreLabel != null)
                row.scoreLabel.text = score;
            if (row.metaLabel != null)
                row.metaLabel.text = meta;
        }

        /// <summary>
        /// Selects a row and reapplies visuals even when the index is unchanged, which
        /// <see cref="Select"/> deliberately skips. Needed when a row's content is replaced while it is
        /// already the selected one.
        /// </summary>
        public void ForceSelect(int index)
        {
            if (rows.Length == 0)
                return;

            selectedIndex = Mathf.Clamp(index, 0, rows.Length - 1);
            Refresh();
            SelectionChanged?.Invoke(selectedIndex);
        }

        /// <summary>Returns the map label of the selected row, for the header readout.</summary>
        public string SelectedMapName()
        {
            if (selectedIndex < 0 || selectedIndex >= rows.Length)
                return string.Empty;

            var label = rows[selectedIndex].mapLabel;
            return label != null ? label.text : string.Empty;
        }
    }
}
