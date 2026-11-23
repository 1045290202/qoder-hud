using System;
using System.Windows.Media;

namespace QoderHud
{
    enum TaskState
    {
        Running,
        NeedsInput,
        Done
    }

    sealed class HudTask
    {
        public string SessionId = "";
        public string Title = "";
        public string Prompt = "";
        public string Cwd = "";
        public string Model = "";
        public string Origin = "Unknown";
        public string OriginChain = "";
        public string Hint = "";
        public string PendingTool = "";
        public string LastTool = "";
        public string LastToolDetail = "";
        public string LastMessage = "";
        public TaskState State = TaskState.Running;
        public DateTime StartedAt = DateTime.Now;
        public DateTime StateSince = DateTime.Now;
        public DateTime DoneAt = DateTime.MinValue;
        public DateTime LastEvent = DateTime.Now;
        public DateTime DonePulseStart = DateTime.MinValue;
        public bool TitleReal;
        public bool TitleBusy;
        public bool TitleAgain;
    }

    sealed class FeedItem
    {
        public DateTime Time;
        public string SessionId;
        public string Text;
        public Color Color;

        public FeedItem(DateTime time, string sessionId, string text, Color color)
        {
            Time = time;
            SessionId = sessionId;
            Text = text;
            Color = color;
        }
    }
}
