using Mapster;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Domain.Entities;

namespace PersonalAIAssistant.Application.Mappings
{
    public class MapObject : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<Message, MessageResponse>()
                  .Map(dest => dest.Role, src => src.UserId == null ? "AI" : "User");

            config.NewConfig<Conversation, ConversationResponse>()
                .Map(dest => dest.AvatarUrl, src => src.ModelsCharacter.AvatarUrl ?? string.Empty);


            //config.NewConfig<List<Conversation>, List<ConversationResponse>>()
            //      .Map(dest => dest., src => src.ModelsCharacter.AvatarUrl ?? string.Empty);
        }
    }
}
