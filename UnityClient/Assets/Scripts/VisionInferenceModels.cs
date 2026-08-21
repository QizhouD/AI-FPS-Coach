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

    [Serializable]
    public sealed class VisionRecommendedAim
    {
        public float x = 0.5f;
        public float y = 0.5f;
        public string target_id;
        public float confidence;
        public float offset_x;
        public float offset_y;
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
    }
}
