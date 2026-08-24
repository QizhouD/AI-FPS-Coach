using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FpsAiCoach
{
    /// <summary>
    /// Right-rail data cards. Metric bars are RectTransforms scaled from their left pivot, so a
    /// score change is a single layout write with no allocation.
    /// </summary>
    public sealed class InsightsController : MonoBehaviour
    {
        [Serializable]
        public sealed class MetricBar
        {
            public RectTransform fill;
            public TMP_Text nameLabel;
            public TMP_Text valueLabel;
            public Image fillImage;
        }

        [Serializable]
        public sealed class InsightCard
        {
            public Image indicator;
            public TMP_Text titleLabel;
            public TMP_Text bodyLabel;
        }

        [SerializeField] private MetricBar[] metrics = Array.Empty<MetricBar>();
        [SerializeField] private InsightCard[] cards = Array.Empty<InsightCard>();

        [Tooltip("Width of a full metric bar, in canvas units.")]
        [SerializeField] private float fullBarWidth = 1660f;

        public int MetricCount => metrics != null ? metrics.Length : 0;

        public void Configure(MetricBar[] configuredMetrics, InsightCard[] configuredCards, float barWidth)
        {
            metrics = configuredMetrics ?? Array.Empty<MetricBar>();
            cards = configuredCards ?? Array.Empty<InsightCard>();
            fullBarWidth = barWidth;
        }

        public void SetMetric(int index, float normalized)
        {
            SetMetric(index, normalized, null);
        }

        /// <summary>
        /// Sets a bar's fill and its readout. Pass <paramref name="display"/> whenever the number the
        /// user should read is not the normalized percentage: a K/D of 1.46 fills 73% of the bar but
        /// must still print as "1.46".
        /// </summary>
        public void SetMetric(int index, float normalized, string display)
        {
            if (metrics == null || index < 0 || index >= metrics.Length)
                return;

            var metric = metrics[index];
            if (metric == null)
                return;

            var value = Mathf.Clamp01(normalized);

            if (metric.fill != null)
            {
                var size = metric.fill.sizeDelta;
                metric.fill.sizeDelta = new Vector2(fullBarWidth * value, size.y);
            }

            if (metric.valueLabel != null)
            {
                metric.valueLabel.text = string.IsNullOrEmpty(display)
                    ? Mathf.RoundToInt(value * 100f).ToString()
                    : display;
            }
        }

        /// <summary>Renames a bar, for when the loaded data changes what the number means.</summary>
        public void SetMetricLabel(int index, string label)
        {
            if (metrics == null || index < 0 || index >= metrics.Length)
                return;

            var metric = metrics[index];
            if (metric?.nameLabel != null)
                metric.nameLabel.text = label;
        }

        public void SetMetrics(IList<float> values)
        {
            if (values == null)
                return;

            var count = Mathf.Min(values.Count, MetricCount);
            for (var index = 0; index < count; index++)
                SetMetric(index, values[index]);
        }

        public void SetCard(int index, string title, string body, bool highPriority, Color priorityColor, Color normalColor)
        {
            if (cards == null || index < 0 || index >= cards.Length)
                return;

            var card = cards[index];
            if (card == null)
                return;

            if (card.titleLabel != null)
                card.titleLabel.text = title;
            if (card.bodyLabel != null)
                card.bodyLabel.text = body;
            if (card.indicator != null)
                card.indicator.color = highPriority ? priorityColor : normalColor;
        }
    }
}
