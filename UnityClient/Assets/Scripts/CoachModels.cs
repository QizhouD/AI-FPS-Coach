using System;

namespace FpsAiCoach
{
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

    /// <summary>
    /// The same payload as <see cref="DemoAnalysisResponse"/> with the playback track left out.
    ///
    /// A full pro demo answers with roughly 6,000 sampled frames of 10 players each, which is about
    /// 9 MB of JSON and 60,000 objects once deserialized. The war-room rails only ever read the
    /// header, the player line and the insights, so this type omits <c>playback</c> entirely and
    /// JsonUtility skips those keys instead of allocating them. Deserialize into
    /// <see cref="DemoAnalysisResponse"/> only once something actually draws the 2D replay.
    /// </summary>
    [Serializable]
    public sealed class DemoReport
    {
        public string analysis_id;
        public string file_name;
        public string map_name;
        public int rounds;
        public string data_source;
        public DemoPlayerStats player = new DemoPlayerStats();
        public DemoInsight[] insights = Array.Empty<DemoInsight>();
    }
}
