using System;

namespace FpsAiCoach
{
    [Serializable]
    public sealed class VisionActualCrosshair
    {
        public float x = 0.5f;
        public float y = 0.5f;
        public float confidence;
        public bool visible;
        public string source = "none";
    }

    [Serializable]
    public sealed class VisionEnemy
    {
        public string id;
        public string team;
        public string part;
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float confidence;
    }

    /// <summary>
    /// The head nearest the crosshair and the deviation to it.
    ///
    /// <c>x</c> and <c>y</c> are smoothed for the overlay marker; the offsets are the raw
    /// measurement. The service sends the offsets as null when no head is visible, which
    /// JsonUtility reads back as zero, so always gate on <c>target_id</c> before trusting them.
    /// </summary>
    [Serializable]
    public sealed class VisionRecommendedAim
    {
        public float x = 0.5f;
        public float y = 0.5f;
        public string target_id;
        public float confidence;
        public float offset_x;
        public float offset_y;
        /// Positive means the crosshair has to move right to reach the target.
        public float offset_deg_x;
        /// Positive means the crosshair has to move up, so the player was aiming low.
        public float offset_deg_y;
        public float offset_deg;
        /// Either "head" for a detected head box or "inferred_head" for one placed from a body box.
        public string target_source;
    }

    [Serializable]
    public sealed class VisionFrameResponse
    {
        public float timestamp;
        public int frame_index;
        public int frame_width;
        public int frame_height;
        public VisionActualCrosshair actual_crosshair = new VisionActualCrosshair();
        public VisionEnemy[] enemies = Array.Empty<VisionEnemy>();
        public VisionRecommendedAim recommended_aim = new VisionRecommendedAim();
        public float inference_ms;
        public VisionDiagnostics diagnostics = new VisionDiagnostics();
    }

    [Serializable]
    public sealed class VisionDiagnostics
    {
        public string enemy_model;
        public string crosshair_model;
    }

    [Serializable]
    public sealed class VisionVideoJobRequest
    {
        public string video_path;
        public string session_id = "unity-video";
        public float sample_rate = 5f;
        /// The in-game FOV the footage was recorded at, horizontal at 4:3.
        public float fov_deg = 90f;
        public float tracking_threshold_deg = 5f;
        public bool detect_shots = true;
    }

    [Serializable]
    public sealed class VisionVideoJobResponse
    {
        public string job_id;
        public string status;
        public float progress;
        public int processed_frames;
        public int total_frames;
        public string error;
        public VisionFrameResponse[] results = Array.Empty<VisionFrameResponse>();
        public VisionSessionMetrics metrics;
    }

    [Serializable]
    public sealed class VisionDeviationStats
    {
        public int count;
        public float mean_deg;
        public float median_deg;
        public float p90_deg;
        public float std_deg;
        public float min_deg;
        public float max_deg;
    }

    [Serializable]
    public sealed class VisionBiasStats
    {
        public int count;
        public float mean_deg;
        public float median_deg;
        public float std_deg;
        public float positive_ratio;
        /// One of aims_low, aims_high, aims_left, aims_right or neutral.
        public string direction = "neutral";
    }

    [Serializable]
    public sealed class VisionTrackingStats
    {
        public float threshold_deg = 5f;
        public int frames_on_target;
        public int frames_with_target;
        public float on_target_ratio;
        public float on_target_seconds;
        public float target_visible_seconds;
    }

    [Serializable]
    public sealed class VisionShotStats
    {
        public int detected_shots;
        public int aligned_shots;
        public VisionDeviationStats deviation = new VisionDeviationStats();
        public VisionBiasStats vertical_bias = new VisionBiasStats();
        public float mean_reaction_seconds;
        /// How many engagements the reaction figure averages over. One is not an average.
        public int reaction_samples;
        public int overcorrection_count;
        public float overcorrection_ratio;
        public string source = "none";
        public string message;
    }

    /// <summary>
    /// Session-level aim diagnostics. Deliberately omits the per-shot list and the histogram:
    /// the rails only read the headline figures, and JsonUtility skips keys it has no field for.
    /// </summary>
    [Serializable]
    public sealed class VisionSessionMetrics
    {
        public string session_id;
        public string job_id;
        public float duration_seconds;
        public float fov_deg;
        public int sampled_frames;
        public int frames_with_target;
        public float target_visibility_ratio;
        public VisionDeviationStats placement_deviation = new VisionDeviationStats();
        public VisionBiasStats vertical_bias = new VisionBiasStats();
        public VisionBiasStats horizontal_bias = new VisionBiasStats();
        public VisionTrackingStats effective_tracking = new VisionTrackingStats();
        public VisionShotStats shots = new VisionShotStats();
        public string headline;
    }
}
