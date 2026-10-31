using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Domain.Entities;

namespace PersonalAIAssistant.Application.Interfaces.Infrastructure
{
    public interface IAIService
    {
        Task<AiReply> GenerateReply(
            Conversation conversation,
            ModelsCharacter character,
            List<Message> recentMessages,
            string newUserMessage);

        Task<string> SummarizeConversation(
            string? currentSummary,
            List<Message> recentMessages);
    }
}
