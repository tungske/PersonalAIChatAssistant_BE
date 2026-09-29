using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Entities;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;
using PersonalAIAssistant.Application.DTOs.Response;

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

        public async Task<string> GenerateReply(
                                Conversation conversation,
                                ModelsCharacter character,
                                List<Message> recentMessages,
                                string newUserMessage)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            ArgumentNullException.ThrowIfNull(character);

            var promptTemplate = await _unitOfWork.PromptRepository.getPromptValueByName("character_config");

            if (string.IsNullOrWhiteSpace(promptTemplate))
                throw new InvalidOperationException("Prompt template not found.");

            var prompt = promptTemplate
                .Replace("{CharacterName}", character.Name)
                .Replace("{CharacterPersonality}", character.Personality)
                .Replace("{CharacterBackstory}", character.Backstory)
                .Replace("{ConversationSummary}",
                    conversation.Summary ?? "No previous context.");

            // Create a system instruction content that provides the AI model with the character's context and conversation summary.
            var systemInstruction = new Content
            {
                Role = "system",
                Parts = new List<Part> { new Part { Text = prompt } }
            };

            var contents = new List<Content>();

            // Add recent messages to the contents list, formatting them with timestamps and speaker information.
            foreach (var msg in recentMessages)
            {
                contents.Add(new Content
                {
                    Role = msg.ModelCharacterId.HasValue ? "model" : "user",
                    Parts = new List<Part>
                    {
                        new Part
                        {
                            Text =
                                $"[Time: {msg.CreatedAt:O}] " +
                                $"[Speaker: {(msg.ModelCharacterId.HasValue ? "MODEL" : "USER")}] " +
                                $"{msg.Content}"
                        }
                    }
                });
            }

            contents.Add(new Content
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new Part
                    {
                        Text =
                            $"[Time: {DateTime.UtcNow:O}] " +
                            $"[Speaker: User] " +
                            $"{newUserMessage}"
                    }
                }
            });

            // Configure the generation parameters for the AI model.
            var config = new GenerateContentConfig
            {
                SystemInstruction = systemInstruction,
                Temperature = 0.8f,
                TopP = 0.95f,
                MaxOutputTokens = 500
            };

            var messageResponse = await GenerateWithFallbackAsync(
                contents,
                config);
            Console.WriteLine("Message: " + messageResponse);
            return messageResponse;
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