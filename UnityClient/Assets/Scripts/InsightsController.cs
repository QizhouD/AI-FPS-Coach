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
                metric.valueLabel.text = Mathf.RoundToInt(value * 100f).ToString();
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
