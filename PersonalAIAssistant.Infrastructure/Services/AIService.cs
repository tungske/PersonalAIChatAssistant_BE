using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Entities;
using PersonalAIAssistant.Domain.Enums;
using System.Text.Json;

namespace PersonalAIAssistant.Infrastructure.Services
{
    public class AIService : IAIService
    {
        // Gemini model is used for generating replies and summarizing conversations.
        private readonly Client _client;
        private readonly string _modelName;
        private readonly string _fallbackModel;
        private readonly IUnitOfWork _unitOfWork;

        public AIService(IConfiguration configuration, IUnitOfWork unitOfWork)
        {
            _client = new Client(apiKey: configuration["GenerativeAI:AIAPIKey"]);

            _modelName =
                configuration["GenerativeAI:PrimaryModel"]
                ?? "gemini-3.5-flash";

            _fallbackModel =
                configuration["GenerativeAI:FallbackModel"]
                ?? "gemini-2.5-flash";

            _unitOfWork = unitOfWork;
        }

        private bool IsRetryableException(Exception ex)
        {
            var message = ex.ToString().ToLowerInvariant();

            return message.Contains("429")
                || message.Contains("resource_exhausted")
                || message.Contains("quota")
                || message.Contains("rate limit")
                || message.Contains("too many requests")
                || message.Contains("overloaded")
                || message.Contains("high demand");
        }

        private async Task<GenerateContentResponse> GenerateWithRetryAsync(
            string modelName,
            List<Content> contents,
            GenerateContentConfig config)
        {
            const int maxRetries = 3;


            // Implementing an exponential backoff retry mechanism for handling transient errors when calling the AI model.
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await _client.Models.GenerateContentAsync(
                        modelName,
                        contents,
                        config);
                }
                catch (Exception ex)
                {
                    if (!IsRetryableException(ex))
                    {
                        throw;
                    }

                    if (attempt == maxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }

            throw new InvalidOperationException(
                "Unexpected retry failure.");
        }

        private async Task<string> GenerateWithFallbackAsync(
            List<Content> contents,
            GenerateContentConfig config)
        {
            try
            {
                var response = await GenerateWithRetryAsync(
                    _modelName,
                    contents,
                    config);

                return response.Text;
            }
            catch (Exception ex)
            {
                if (!IsRetryableException(ex))
                {
                    throw;
                }

                var fallbackResponse =
                    await GenerateWithRetryAsync(
                        _fallbackModel,
                        contents,
                        config);

                return fallbackResponse.Text;
            }
        }



        public async Task<AiReply> GenerateReply(
            Conversation conversation,
            ModelsCharacter character,
            List<Message> recentMessages,
            string newUserMessage)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            ArgumentNullException.ThrowIfNull(character);

            var promptTemplate = await _unitOfWork.PromptRepository
                .getPromptValueByName("character_config");
            if (string.IsNullOrWhiteSpace(promptTemplate))
                throw new InvalidOperationException("Prompt template not found.");

            var prompt = promptTemplate
                .Replace("{CharacterName}", character.Name, StringComparison.OrdinalIgnoreCase)
                .Replace("{CharacterPersonality}", character.Personality, StringComparison.OrdinalIgnoreCase)
                .Replace("{CharacterBackstory}", character.Backstory, StringComparison.OrdinalIgnoreCase)
                .Replace("{ConversationSummary}", conversation.Summary ?? "No previous context.",
                         StringComparison.OrdinalIgnoreCase);

            var systemInstruction = new Content
            {
                Parts = new List<Part> { new() { Text = prompt } }
            };

            // History: chỉ text thuần, role đã đủ để phân biệt
            var contents = recentMessages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new Content
                {
                    Role = m.ModelCharacterId.HasValue ? "model" : "user",
                    Parts = new List<Part> { new() { Text = m.Content } }
                })
                .ToList();

            contents.Add(new Content
            {
                Role = "user",
                Parts = new List<Part> { new() { Text = newUserMessage } }
            });

            var emotions = Enum.GetNames<EmotionName>().ToList();

            var config = new GenerateContentConfig
            {
                SystemInstruction = systemInstruction,
                Temperature = 0.8f,
                TopP = 0.95f,
                MaxOutputTokens = 1000,
                ResponseMimeType = "application/json",
                ResponseSchema = new Schema
                {
                    Type = Google.GenAI.Types.Type.Object,
                    Properties = new Dictionary<string, Schema>
                    {
                        ["content"] = new() { Type = Google.GenAI.Types.Type.String },
                        ["emotion"] = new() { Type = Google.GenAI.Types.Type.String, Enum = emotions },
                        ["emotionScore"] = new() { Type = Google.GenAI.Types.Type.Integer }
                    },
                    Required = new List<string> { "content", "emotion", "emotionScore" }
                }
            };

            var json = await GenerateWithFallbackAsync(contents, config);

            try
            {
                var reply = JsonSerializer.Deserialize<AiReply>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (reply is null || string.IsNullOrWhiteSpace(reply.Content))
                    throw new JsonException("Empty reply");
                return reply;
            }
            catch (JsonException)
            {
                // Không để một lần model lỗi làm hỏng luồng chat
                return new AiReply { Content = json, Emotion = EmotionName.Neutral, EmotionScore = 5 };
            }
        }
        

        public async Task<string> SummarizeConversation(string? currentSummary, List<Message> recentMessages)
        {
            var formattedMessages = string.Join("\n", recentMessages.Select(m =>
                m.UserId.HasValue ? $"User: {m.Content}" : $"Assistant: {m.Content}"));

            var promptemplate = await _unitOfWork.PromptRepository.getPromptValueByName("summary_config");

            promptemplate = promptemplate.Replace("{currentSummary}", currentSummary ?? "None")
                                         .Replace("{formattedMessages}", formattedMessages);

            var contents = new List<Content>
            {
                new Content
                {
                    Role = "user",
                    Parts = new List<Part>
                    {
                        new Part
                        {
                            Text = promptemplate
                        }
                    }
                }
            };

            var config = new GenerateContentConfig
            {
                Temperature = 0.3f,
                MaxOutputTokens = 300
            };

            return await GenerateWithFallbackAsync(
                contents,
                config);
        }
    }
}