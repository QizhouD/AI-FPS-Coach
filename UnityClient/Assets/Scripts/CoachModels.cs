using System;

namespace FpsAiCoach
{
    [Serializable]
    public sealed class CoachScores
    {
        public int aim = 68;
        public int positioning = 72;
        public int decision = 70;
        public int consistency = 65;
    }

    [Serializable]
    public sealed class CoachTip
    {
        public string severity = "info";
        public string title = "Waiting for analysis";
        public string message = "Start a video source to receive real-time coaching tips.";
        public string action = "Confirm that OBS Virtual Camera is running.";
    }

    [Serializable]
    public sealed class AnalysisResponse
    {
        public string session_id;
        public string timestamp;
        public CoachScores scores = new CoachScores();
        public CoachTip tip = new CoachTip();
    }

    [Serializable]
    public sealed class DemoPlayerStats
    {
        public string name = "--";
        public int kills;
        public int deaths;
        public int assists;
        public int headshots;
        public float headshot_percentage;
        public float kd_ratio;
        public int damage;
        public float adr;
        public int opening_kills;
        public int opening_deaths;
    }

    [Serializable]
    public sealed class DemoInsight
    {
        public string severity;
        public string title;
        public string evidence;
        public string action;
    }

    [Serializable]
    public sealed class DemoPlaybackBounds
    {
        public float min_x;
        public float max_x = 1f;
        public float min_y;
        public float max_y = 1f;
    }

    [Serializable]
    public sealed class DemoPlaybackPlayer
    {
        public string id;
        public string name;
        public int team;
        public float x;
        public float y;
        public int health;
        public bool alive;
        public float yaw;
    }

    [Serializable]
    public sealed class DemoPlaybackFrame
    {
        public int tick;
        public float time;
        public int round;
        public DemoPlaybackPlayer[] players = Array.Empty<DemoPlaybackPlayer>();
    }

    [Serializable]
    public sealed class DemoPlayback
    {
        public float duration;
        public float tick_rate = 64f;
        public float sample_rate = 2f;
        public string coordinate_space = "world";
        public DemoPlaybackBounds bounds = new DemoPlaybackBounds();
        public DemoPlaybackFrame[] frames = Array.Empty<DemoPlaybackFrame>();
    }

    [Serializable]
    public sealed class DemoAnalysisResponse
    {
        public string analysis_id;
        public string file_name;
        public string map_name;
        public int rounds;
        public string data_source;
        public DemoPlayerStats player = new DemoPlayerStats();
        public DemoInsight[] insights = Array.Empty<DemoInsight>();
        public DemoPlayback playback = new DemoPlayback();
    }
}
