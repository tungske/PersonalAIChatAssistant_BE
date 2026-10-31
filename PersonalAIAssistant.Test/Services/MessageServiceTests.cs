using FluentAssertions;
using Moq;
using MockQueryable;
using MockQueryable.Moq;
using PersonalAIAssistant.Application.DTOs.Request;
using PersonalAIAssistant.Application.DTOs.Response;
using PersonalAIAssistant.Application.Interfaces.Infrastructure;
using PersonalAIAssistant.Application.Services;
using PersonalAIAssistant.Domain;
using PersonalAIAssistant.Domain.Enums;
using Xunit;
using PersonalAIAssistant.Domain.Entities;

namespace PersonalAIAssistant.Test.Services
{
    public class MessageServiceTests
    {
        private readonly Mock<IUnitOfWork> _unitOfWorkMock;
        private readonly Mock<ITokenService> _tokenServiceMock;
        private readonly Mock<IAIService> _aiServiceMock;
        private readonly Mock<ICacheService> _cacheServiceMock;
        private readonly MessageService _messageService;

        public MessageServiceTests()
        {
            _unitOfWorkMock = new Mock<IUnitOfWork>();
            _tokenServiceMock = new Mock<ITokenService>();
            _aiServiceMock = new Mock<IAIService>();
            _cacheServiceMock = new Mock<ICacheService>();

            _messageService = new MessageService(
                _unitOfWorkMock.Object,
                _tokenServiceMock.Object,
                _aiServiceMock.Object,
                _cacheServiceMock.Object);
        }

        [Fact]
        public async Task GetMessagesFromConversation_ShouldReturnMessagesForCurrentUserOnly()
        {
            // Arrange
            var userId = 1;
            var conversationId = 10;
            var conversation = new Conversation
            {
                Id = conversationId,
                UserId = userId,
                ModelCharacterId = 7,
                Status = Domain.Enums.ConversationStatus.Active,
                ModelsCharacter = new ModelsCharacter
                {
                    Id = 7,
                    Name = "Test Character",
                    AvatarUrl = "avatar.png",
                    Backstory = "Backstory",
                    Personality = "Neutral",
                    SpeakingStyle = "Normal",
                    IntelligenceLevel = "Medium",
                    ResponseStyle = "Balanced",
                    ExampleDialogue = "Hello"
                }
            };

            var messages = new List<Message>
            {
                new Message { Id = 1, ConversationId = conversationId, UserId = userId, Content = "hello", CreatedAt = DateTime.UtcNow },
                new Message { Id = 2, ConversationId = conversationId, UserId = userId, Content = "how are you", CreatedAt = DateTime.UtcNow.AddMinutes(1) }
            };

            _tokenServiceMock.Setup(x => x.GetUserId()).Returns(userId);
            _unitOfWorkMock.Setup(x => x.ConversationRepository.GetAll()).Returns(new List<Conversation> { conversation }.BuildMock());
            _unitOfWorkMock.Setup(x => x.MessageRepository.GetAll()).Returns(messages.BuildMock());
            _cacheServiceMock.Setup(x => x.GetAsync<MessageListResponse>($"chat:{conversationId}:latest:10:user:{userId}"))
                .ReturnsAsync((MessageListResponse?)null);

            // Act
            var result = await _messageService.GetMessagesFromConversation(conversationId, 10);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Messages.Should().HaveCount(2);
            result.Data.Messages[0].Content.Should().Be("hello");
            result.Data.FirstMessageId.Should().Be(1);
            _cacheServiceMock.Verify(x => x.SetAsync(
                $"chat:{conversationId}:latest:10:user:{userId}",
                It.IsAny<MessageListResponse>(),
                It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task GetMessagesFromConversation_WhenLimitExceedsDefault_ShouldRejectRequest()
        {
            // Arrange
            _tokenServiceMock.Setup(x => x.GetUserId()).Returns(1);

            // Act
            var result = await _messageService.GetMessagesFromConversation(10, 999);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Be("Limit cannot exceed 50");
        }

        [Fact]
        public async Task CreateMessage_WhenConversationBelongsToUser_ShouldCreateUserAndAiMessages()
        {
            // Arrange
            var userId = 1;
            var conversationId = 20;
            var characterId = 7;

            var conversation = new Conversation
            {
                Id = conversationId,
                UserId = userId,
                ModelCharacterId = characterId,
                Status = Domain.Enums.ConversationStatus.Active,
                Summary = "old summary"
            };

            var character = new ModelsCharacter
            {
                Id = characterId,
                Name = "Assistant",
                Backstory = "Story",
                Personality = "Friendly",
                AvatarUrl = "avatar.png",
                SpeakingStyle = "Casual",
                IntelligenceLevel = "High",
                ResponseStyle = "Balanced",
                ExampleDialogue = "Hi"
            };

            var existingMessages = new List<Message>
            {
                new Message { Id = 99, ConversationId = conversationId, UserId = userId, Content = "previous message", CreatedAt = DateTime.UtcNow }
            };

            _tokenServiceMock.Setup(x => x.GetUserId()).Returns(userId);
            _unitOfWorkMock.Setup(x => x.ConversationRepository.GetAll()).Returns(new List<Conversation> { conversation }.BuildMock());
            _unitOfWorkMock.Setup(x => x.ModelRepository.GetAll()).Returns(new List<ModelsCharacter> { character }.BuildMock());
            _unitOfWorkMock.Setup(x => x.MessageRepository.GetAll()).Returns(existingMessages.BuildMock());
            _unitOfWorkMock.Setup(x => x.CharacterEmotionsRepository.GetAll())
                .Returns(new List<CharacterEmotion>().BuildMock());
            _aiServiceMock.Setup(x => x.GenerateReply(conversation, character, It.IsAny<List<Message>>(), "hello"))
                .ReturnsAsync(new AiReply
                {
                    Content = "AI reply",
                    Emotion = EmotionName.Happy,
                    EmotionScore = 8
                });
            _unitOfWorkMock.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>()))
                .Returns((Func<Task> operation) => operation());
            _unitOfWorkMock.Setup(x => x.MessageRepository.AddAsync(It.IsAny<Message>())).Returns(Task.CompletedTask);
            _unitOfWorkMock.Setup(x => x.ConversationRepository.Update(conversation)).Returns(Task.CompletedTask);

            var request = new MessageRequest { ConversationId = conversationId, Content = "hello" };

            // Act
            var result = await _messageService.CreateMessage(request);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Content.Should().Be("AI reply");
            result.Data.ConversationId.Should().Be(conversationId);
            result.Data.emotion.Should().Be(nameof(EmotionName.Happy));
            _unitOfWorkMock.Verify(x => x.MessageRepository.AddAsync(It.IsAny<Message>()), Times.Exactly(2));
            _unitOfWorkMock.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>()), Times.Once);
        }

        [Fact]
        public async Task CreateMessage_WhenConversationDoesNotBelongToUser_ShouldThrowKeyNotFound()
        {
            // Arrange
            _tokenServiceMock.Setup(x => x.GetUserId()).Returns(1);
            _unitOfWorkMock.Setup(x => x.ConversationRepository.GetAll())
                .Returns(new List<Conversation>
                {
                    new Conversation { Id = 5, UserId = 2, ModelCharacterId = 7, Status = Domain.Enums.ConversationStatus.Active }
                }.BuildMock());

            var request = new MessageRequest { ConversationId = 5, Content = "hello" };

            // Act
            var result = await _messageService.CreateMessage(request);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Conversation not found or does not belong to user");
        }

        [Fact]
        public async Task CreateMessage_WhenContentIsEmpty_ShouldRejectRequestWithoutCallingAi()
        {
            var result = await _messageService.CreateMessage(
                new MessageRequest { ConversationId = 20, Content = " " });

            result.Success.Should().BeFalse();
            result.Message.Should().Be("Message content is required.");
            _aiServiceMock.Verify(
                x => x.GenerateReply(
                    It.IsAny<Conversation>(),
                    It.IsAny<ModelsCharacter>(),
                    It.IsAny<List<Message>>(),
                    It.IsAny<string>()),
                Times.Never);
        }
    }
}
