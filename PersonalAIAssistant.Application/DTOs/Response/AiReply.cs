using PersonalAIAssistant.Domain.Enums;

namespace PersonalAIAssistant.Application.DTOs.Response
{
    public class AiReply
    {
        public string Content { get; set; } = "";
        public EmotionName Emotion { get; set; } = EmotionName.Neutral;
        public int EmotionScore { get; set; }
    }
}
