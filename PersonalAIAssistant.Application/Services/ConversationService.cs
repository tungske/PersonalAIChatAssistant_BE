using Mapster;
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Application.DTOs.Request;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Application.Interfaces.Services;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Base;
using PersonalAIAssistant.Domain.Entities;
using PersonalAIAssistant.Domain.Enums;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;

namespace PersonalAIAssistant.Application.Services
{
    public class ConversationService : IConversationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenService _jwtService;
        private readonly ICacheService _redisCacheService;
        public ConversationService(IUnitOfWork unitOfWork, ITokenService jwtService, ICacheService redisCacheService)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _redisCacheService = redisCacheService;
        }

        public async Task<ApiResponse<List<ConversationResponse>>> GetConversationAsync()
        {
            var userId = _jwtService.GetUserId();

            var cacheKey = $"conversations:user:{userId}";
            var cached = await _redisCacheService.GetAsync<List<ConversationResponse>>(cacheKey);
            if (cached != null)
            {
                return new ApiResponse<List<ConversationResponse>>
                {
                    Success = true,
                    Message = "Conversations retrieved successfully (cache)",
                    Data = cached
                };
            }

            var conversations = await _unitOfWork
                .ConversationRepository
                .GetAll()
                .Where(x => x.UserId == userId)
                .Include(x => x.ModelsCharacter)
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync();

            var response = conversations.Adapt<List<ConversationResponse>>();

            // cache short lived
            await _redisCacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(30));

            return new ApiResponse<List<ConversationResponse>>
            {
                Success = true,
                Message = "Conversations retrieved successfully",
                Data = response
            };
        }

        public async Task<ApiResponse<ConversationResponse>> CreateConversation(ConversationRequest request)
        {
            var userId = _jwtService.GetUserId();

            var modelCharacter = await _unitOfWork.ModelRepository
                .FindAsync(request.ModelCharacterId);

            if (modelCharacter == null)
            {
                throw new KeyNotFoundException("Model character not found");
            }

            var conversationExisted = await _unitOfWork.ConversationRepository
                .GetAll()
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.ModelCharacterId == request.ModelCharacterId);

            if (conversationExisted != null)
            {
                return new ApiResponse<ConversationResponse>
                {
                    Success = true,
                    Message = "Conversation already exists!",
                    Data = conversationExisted.Adapt<ConversationResponse>()
                };
            }

            var conversation = new Conversation
            {
                UserId = userId,
                ModelCharacterId = request.ModelCharacterId,
                Title = modelCharacter.Name,
                Status = ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.ConversationRepository.AddAsync(conversation);
            await _unitOfWork.SaveChangesAsync();

            // invalidate cache for this user so next read is fresh
            var convCacheKey = $"conversations:user:{userId}";
            await _redisCacheService.RemoveAsync(convCacheKey);

            return new ApiResponse<ConversationResponse>
            {
                Success = true,
                Message = "Created success!",
                Data = conversation.Adapt<ConversationResponse>()
            };
        }

        public async Task<ApiResponse<ConversationResponse>> DeleteConversation(int id)
        {
            try
            {
                var userId = _jwtService.GetUserId();
                var conversationExisted = await _unitOfWork.ConversationRepository.GetAll().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
                if (conversationExisted != null)
                {
                    await _unitOfWork.ConversationRepository.Remove(conversationExisted);
                    await _unitOfWork.SaveChangesAsync();

                    // invalidate cache for this user
                    var convCacheKey = $"conversations:user:{userId}";
                    await _redisCacheService.RemoveAsync(convCacheKey);
                }
            }
            catch (Exception e)
            {
                return new ApiResponse<ConversationResponse>
                {
                    Success = false,
                    Message = e.Message
                };
            }
            return new ApiResponse<ConversationResponse>
            {
                Success = true,
                Message = "Delete success!"
            };
        }


    }
}
