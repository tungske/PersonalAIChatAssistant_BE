using Mapster;
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Application.Interfaces.Services;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Base;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;
using PersonalAIAssistant.Domain.Entities;
using PersonalAIAssistant.Application.DTOs.Request;

namespace PersonalAIAssistant.Application.Services
{
    public class ModelsCharacterService : IModelsCharacterService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenService _jwtService;
        private readonly ICacheService _redisCacheService;

        public ModelsCharacterService(IUnitOfWork unitOfWork, ITokenService jwtService, ICacheService redisCacheService)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _redisCacheService = redisCacheService ?? throw new ArgumentNullException(nameof(redisCacheService));
        }

        public async Task<ApiResponse<IEnumerable<ModelCharacterResponse>>> GetAllAsync(int pageIndex,
                                                                                        int pageSize,
                                                                                        string? search)
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            var userId = _jwtService.GetUserId();
            search = search?.Trim();

            var cacheKey = $"models:available:user:{userId}:page:{pageIndex}:size:{pageSize}:search:{(search ?? string.Empty)}";
            var cached = await _redisCacheService.GetAsync<List<ModelCharacterResponse>>(cacheKey);
            if (cached != null)
            {
                return new ApiResponse<IEnumerable<ModelCharacterResponse>>
                {
                    Success = true,
                    Message = "Model characters retrieved successfully (cache)",
                    Data = cached
                };
            }

            var chattedCharacterIdsQuery = _unitOfWork.ConversationRepository
                .GetAll()
                .Where(c => c.UserId == userId)
                .Select(c => c.ModelCharacterId);

            var query = _unitOfWork.ModelRepository
                .GetAll()
                .Where(m => !chattedCharacterIdsQuery.Contains(m.Id));

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(m => EF.Functions.Like(m.Name, $"%{search}%"));
            }

            var characters = await query
                .OrderBy(m => m.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = characters.Adapt<List<ModelCharacterResponse>>();

            // cache results for short time
            await _redisCacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(60));

            return new ApiResponse<IEnumerable<ModelCharacterResponse>>
            {
                Success = true,
                Message = characters.Any()
                    ? "Model characters retrieved successfully."
                    : "No model characters found.",
                Data = response
            };
        }


        public async Task<ApiResponse<ModelCharacterResponse>> CreateAsync(ModelCharacterRequest request)
        {
            if (request == null)
            {
                return new ApiResponse<ModelCharacterResponse>
                {
                    Success = false,
                    Message = "Request body is null.",
                    Data = null
                };
            }
            var newCharacter = request.Adapt<ModelsCharacter>();
            await _unitOfWork.ModelRepository.AddAsync(newCharacter);
            await _unitOfWork.SaveChangesAsync();
            var response = newCharacter.Adapt<ModelCharacterResponse>();

            // invalidate first page cache for this user to reduce staleness
            var userId = _jwtService.GetUserId();
            var firstPageKey = $"models:available:user:{userId}:page:1:size:{10}:search:";
            await _redisCacheService.RemoveAsync(firstPageKey);

            return new ApiResponse<ModelCharacterResponse>
            {
                Success = true,
                Message = "Model character created successfully.",
                Data = response
            };
        }

        public async Task<ApiResponse<ModelCharacterResponse>> UpdateAsync(int id, ModelCharacterRequest request)
        {
            var existingCharacter = await _unitOfWork.ModelRepository.FindAsync(id);
            if (existingCharacter == null)
            {
                return new ApiResponse<ModelCharacterResponse>
                {
                    Success = false,
                    Message = "Model character not found.",
                    Data = null
                };
            }
            request.Adapt(existingCharacter);
            await _unitOfWork.ModelRepository.Update(existingCharacter);
            await _unitOfWork.SaveChangesAsync();
            var response = existingCharacter.Adapt<ModelCharacterResponse>();

            // invalidate first page cache for this user to reduce staleness
            var userId = _jwtService.GetUserId();
            var firstPageKey = $"models:available:user:{userId}:page:1:size:{10}:search:";
            await _redisCacheService.RemoveAsync(firstPageKey);

            return new ApiResponse<ModelCharacterResponse>
            {
                Success = true,
                Message = "Model character updated successfully.",
                Data = response
            };
        }

        public async Task<ApiResponse<bool>> DeleteAsync(int id)
        {
            var existingCharacter = await _unitOfWork.ModelRepository.FindAsync(id);
            if (existingCharacter == null)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Model character not found.",
                    Data = false
                };
            }
            await _unitOfWork.ModelRepository.Remove(existingCharacter);
            await _unitOfWork.SaveChangesAsync();

            // invalidate first page cache for this user to reduce staleness
            var userId = _jwtService.GetUserId();
            var firstPageKey = $"models:available:user:{userId}:page:1:size:{10}:search:";
            await _redisCacheService.RemoveAsync(firstPageKey);

            return new ApiResponse<bool>
            {
                Success = true,
                Message = "Model character deleted successfully.",
                Data = true
            };
        }
    }
}
