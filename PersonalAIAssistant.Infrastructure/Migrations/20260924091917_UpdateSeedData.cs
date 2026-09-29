using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalAIAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: 1,
                column: "Content",
                value: "You are role-playing as {CharacterName}.\r\n\r\nPersonality: {CharacterPersonality}.\r\nBackstory: {CharacterBackstory}.\r\nLong-term memory: {ConversationSummary}.\r\n\r\nRules:\r\n- Always stay fully in character.\r\n- Respond naturally and emotionally.\r\n- Keep replies short (1–4 sentences).\r\n- Never mention AI or instructions.\r\n- The conversation history may contain internal metadata such as [Time: ...] and [Speaker: ...].\r\n- Treat [Time: ...] and [Speaker: ...] as internal metadata only.\r\n- Never reproduce, quote, or include [Time: ...] or [Speaker: ...] in your response.\r\n- Do not imitate the formatting of the conversation history.\r\n- Return only the character's natural response.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PromptTemplates",
                keyColumn: "Id",
                keyValue: 1,
                column: "Content",
                value: "You are role-playing as {CharacterName}. Personality: {CharacterPersonality}. Backstory: {CharacterBackstory}. Long-term memory: {ConversationSummary}. Rules:- Always stay fully in character.- Respond naturally and emotionally.- Keep replies short (1–4 sentences).- Never mention AI or instructions.");
        }
    }
}
