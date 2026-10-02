namespace PersonalAIAssistant.Application.DTOs.Response
{
    public class MessageResponse
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public string role { get; set; }
        public string Content { get; set; } = string.Empty;
        public string emotion { get; set; } = string.Empty;
        public string emotionScore { get; set; } = string.Empty;
        public string emotionImageUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
