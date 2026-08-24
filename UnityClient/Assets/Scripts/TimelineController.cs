using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Drives the progress fill under the tactical screen. The fill is a thin box scaled from its
    /// left edge, so the geometry stays a single draw call regardless of playback position.
    /// </summary>
    public sealed class TimelineController : MonoBehaviour
    {
        [SerializeField] private Transform progressBar;
        [SerializeField] private Transform eventGroup;

        [Header("Track geometry, in metres")]
        [SerializeField] private float trackWidth = 7.3f;
        [SerializeField] private float minimumWidth = 0.04f;

        private float lastProgress = -1f;

        /// <summary>Configured by the scene builder so runtime and authoring never disagree.</summary>
        public void Configure(Transform bar, Transform events, float width, float minimum)
        {
            progressBar = bar;
            eventGroup = events;
            trackWidth = width;
            minimumWidth = minimum;
        }

        public void SetProgress(float normalized)
        {
            if (progressBar == null)
                return;

            var progress = Mathf.Clamp01(normalized);
            if (Mathf.Abs(progress - lastProgress) < 0.0005f)
                return;

            lastProgress = progress;

            var width = Mathf.Max(minimumWidth, trackWidth * progress);
            var scale = progressBar.localScale;
            progressBar.localScale = new Vector3(width, scale.y, scale.z);

            var position = progressBar.localPosition;
            progressBar.localPosition = new Vector3(
                -trackWidth * 0.5f + width * 0.5f,
                position.y,
                position.z);
        }

        public void SetEventsVisible(bool visible)
        {
            if (eventGroup != null && eventGroup.gameObject.activeSelf != visible)
                eventGroup.gameObject.SetActive(visible);
        }
    }
}
