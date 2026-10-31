using Mapster;
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Application.DTOs.Request;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Application.Interfaces.Services;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Base;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;
using PersonalAIAssistant.Domain.Entities;

namespace PersonalAIAssistant.Application.Services
{
    public class MessageService : IMessageService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenService _jwtService;
        private readonly IAIService _generationAIService;
        private readonly ICacheService _redisCacheService;

        public MessageService(IUnitOfWork unitOfWork, ITokenService jwtService, IAIService generationAIService, ICacheService redisCacheService)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _generationAIService = generationAIService;
            _redisCacheService = redisCacheService;
        }

        private readonly int _defaultLimit = 50;

        public async Task<ApiResponse<MessageListResponse>> GetMessagesFromConversation(
            int conversationId,
            int limit,
            long? beforeMessageId = null)
        {

            var userId = _jwtService.GetUserId();

            if (limit <= 0)
            {
                limit = _defaultLimit;
            }

            if (limit > _defaultLimit)
            {
                return new ApiResponse<MessageListResponse>
                {
                    Success = false,
                    Message = $"Limit cannot exceed {_defaultLimit}"
                };
            }

            // Validate conversation exists and belongs to current user
            var conversation = await _unitOfWork.ConversationRepository
                .GetAll()
                .Include(x => x.ModelsCharacter)
                .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserId == userId);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found!");
            }

            if (beforeMessageId == null)
            {
                var cacheKey = $"chat:{conversationId}:latest:{limit}:user:{userId}";

                var cachedResponse = await _redisCacheService
                    .GetAsync<MessageListResponse>(cacheKey);

                if (cachedResponse != null)
                {
                    return new ApiResponse<MessageListResponse>
                    {
                        Success = true,
                        Message = "Data from cache",
                        Data = cachedResponse
                    };
                }
            }

            var query = _unitOfWork.MessageRepository
                .GetAll()
                .Where(x => x.ConversationId == conversationId);

            if (beforeMessageId != null)
            {
                query = query.Where(x => x.Id < beforeMessageId);
            }

            var messages = await query
                .OrderByDescending(x => x.Id)
                .Take(limit)
                .ToListAsync();

            messages = messages
                .Take(limit)
                .OrderBy(x => x.Id)
                .ToList();

            var response = new MessageListResponse
            {
                ModelId = conversation.ModelCharacterId.ToString(),
                ModelName = conversation.ModelsCharacter?.Name,
                ModelAvatarUrl = conversation.ModelsCharacter?.AvatarUrl,
                FirstMessageId = messages.FirstOrDefault()?.Id,

                Messages = messages.Adapt<List<MessageResponse>>()
            };


            if (beforeMessageId == null)
            {
                var cacheKey = $"chat:{conversationId}:latest:{limit}:user:{userId}";

                await _redisCacheService.SetAsync(
                    cacheKey,
                    response,
                    TimeSpan.FromSeconds(30));
            }

            return new ApiResponse<MessageListResponse>
            {
                Success = true,
                Message = messages.Any()
                    ? "Messages retrieved successfully"
                    : "No messages found in this conversation",
                Data = response
            };
        }


        public async Task<ApiResponse<MessageResponse>> CreateMessage(MessageRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Content))
                {
                    return new ApiResponse<MessageResponse>
                    {
                        Success = false,
                        Message = "Message content is required."
                    };
                }

                var userId = _jwtService.GetUserId();

                // Validate conversation exists and belongs to current user
                var conversation = await _unitOfWork.ConversationRepository
                    .GetAll()
                    .FirstOrDefaultAsync(x => x.Id == request.ConversationId && x.UserId == userId);

                if (conversation == null)
                {
                    throw new KeyNotFoundException("Conversation not found or does not belong to user");
                }

                // Validate character exists before calling AI
                var character = await _unitOfWork.ModelRepository
                    .GetAll()
                    .FirstOrDefaultAsync(x => x.Id == conversation.ModelCharacterId);

                if (character == null)
                {
                    throw new KeyNotFoundException("Character not found for this conversation");
                }

                // Get recent messages for context before calling AI
                var recentMessages = await _unitOfWork.MessageRepository
                    .GetAll()
                    .Where(x => x.ConversationId == conversation.Id)
                    .OrderByDescending(x => x.Id)
                    .Take(20)
                    .OrderBy(x => x.Id)
                    .ToListAsync();

                var aiReply = await _generationAIService.GenerateReply(
                    conversation,
                    character,
                    recentMessages,
                    request.Content
                );

                if (aiReply == null || string.IsNullOrWhiteSpace(aiReply.Content))
                {
                    throw new InvalidOperationException("The AI service returned an empty reply.");
                }

                // Save both user and AI messages to the database
                var now = DateTime.UtcNow;
                var userMessage = new Message
                {
                    ConversationId = conversation.Id,
                    UserId = userId,
                    Content = request.Content,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                var aiMessage = new Message
                {
                    ConversationId = conversation.Id,
                    ModelCharacterId = character.Id,
                    Content = aiReply.Content,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // Get the emotion details for the AI reply
                var emotion = await _unitOfWork.CharacterEmotionsRepository
                    .GetAll()
                    .FirstOrDefaultAsync(x =>
                        x.CharacterId == character.Id &&
                        x.EmotionName.ToLower() == aiReply.Emotion.ToString().ToLower());

                await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await _unitOfWork.MessageRepository.AddAsync(userMessage);
                    await _unitOfWork.MessageRepository.AddAsync(aiMessage);

                    conversation.UpdatedAt = now;
                    await _unitOfWork.ConversationRepository.Update(conversation);

                    // Update conversation summary every 20 messages
                    var messageCount = await _unitOfWork.MessageRepository
                        .GetAll()
                        .CountAsync(x => x.ConversationId == conversation.Id);

                    if ((messageCount + 2) % 20 == 0)
                    {
                        var messagesToSummarize = recentMessages
                            .Concat(new[] { userMessage, aiMessage })
                            .ToList();
                        conversation.Summary = await _generationAIService.SummarizeConversation(
                            conversation.Summary,
                            messagesToSummarize);
                        await _unitOfWork.ConversationRepository.Update(conversation);
                    }
                });

                var response = new MessageResponse
                {
                    Id = aiMessage.Id,
                    ConversationId = conversation.Id,
                    Role = 1,
                    Content = aiMessage.Content,
                    emotion = aiReply.Emotion.ToString(),
                    emotionScore = aiReply.EmotionScore.ToString(),
                    emotionImageUrl = emotion?.EmotionIconUrl ?? string.Empty,
                    CreatedAt = aiMessage.CreatedAt,
                    UpdatedAt = aiMessage.UpdatedAt
                };

                return new ApiResponse<MessageResponse>
                {
                    Success = true,
                    Message = "Send message successfully!",
                    Data = response
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<MessageResponse>
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task<ApiResponse<string>> DeleteMessage(int messageId)
        {
            try
            {
                var userId = _jwtService.GetUserId();
                var messageExisted = await _unitOfWork.MessageRepository
                    .GetAll()
                    .FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == userId);
                if (messageExisted == null)
                {
                    throw new KeyNotFoundException("Message not found or does not belong to user");
                }
                await _unitOfWork.MessageRepository.Remove(messageExisted);
                await _unitOfWork.SaveChangesAsync();
                return new ApiResponse<string>
                {
                    Success = true,
                    Message = "Delete success!"
                };
            }
            catch (Exception e)
            {
                return new ApiResponse<string>
                {
                    Success = false,
                    Message = e.Message,
                    Data = e.InnerException?.ToString()
                };
            }
        }

        public async Task<ApiResponse<string>> ExportConversationtoPDF(int conversationId)
        {
            try
            {
                var userId = _jwtService.GetUserId();
                var conversationExisted = await _unitOfWork.ConversationRepository
                    .GetAll()
                    .FirstOrDefaultAsync(x => x.Id == conversationId && x.UserId == userId);
                if (conversationExisted == null)
                {
                    return new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Conversation not found"
                    };
                }
                var messages = await _unitOfWork.MessageRepository
                    .GetAll()
                    .Where(x => x.ConversationId == conversationId)
                    .OrderBy(x => x.CreatedAt)
                    .ToListAsync();



                return new ApiResponse<string>
                {
                    Success = true,
                    Data = "exportContent"
                };
            }
            catch (Exception e)
            {
                return new ApiResponse<string>
                {
                    Success = false,
                    Message = e.Message,
                    Data = e.InnerException?.ToString()
                };
            }
        }

    }
}