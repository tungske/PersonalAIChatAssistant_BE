namespace PersonalAIAssistant.Application.DTOs.Response
{
    public class MessageResponse
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public int Role { get; set; } // 0 for User, 1 for AI
        // Mesage content
        public string Content { get; set; } = string.Empty;
        // Character emotion
        public string emotion { get; set; } = string.Empty;
        // Emotion score
        public string emotionScore { get; set; } = string.Empty;
        // Emotion image url from database
        public string emotionImageUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
