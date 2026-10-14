using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arqenion.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiFeedbacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: false),
                    SessionEventId = table.Column<string>(type: "text", nullable: true),
                    StepId = table.Column<int>(type: "integer", nullable: false),
                    DeviationType = table.Column<string>(type: "text", nullable: true),
                    PossibleRisk = table.Column<string>(type: "text", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    RecommendedAction = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "text", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    RequestJson = table.Column<string>(type: "text", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    StepId = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<string>(type: "text", nullable: true),
                    InstrumentCode = table.Column<string>(type: "text", nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    DeviationType = table.Column<string>(type: "text", nullable: true),
                    ScoreChange = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ScenarioId = table.Column<string>(type: "text", nullable: false),
                    ProcedureCode = table.Column<string>(type: "text", nullable: false),
                    CurrentStepId = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    RawScore = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CorrectActions = table.Column<int>(type: "integer", nullable: false),
                    WrongActions = table.Column<int>(type: "integer", nullable: false),
                    SterileViolations = table.Column<int>(type: "integer", nullable: false),
                    HintsUsed = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveErrors = table.Column<int>(type: "integer", nullable: false),
                    ActiveComplicationId = table.Column<string>(type: "text", nullable: true),
                    ComplicationStepIndex = table.Column<int>(type: "integer", nullable: false),
                    ComplicationsTriggered = table.Column<int>(type: "integer", nullable: false),
                    ComplicationsResolved = table.Column<int>(type: "integer", nullable: false),
                    ProcedureModel = table.Column<string>(type: "text", nullable: true),
                    CurrentNodeId = table.Column<string>(type: "text", nullable: true),
                    FlowStateJson = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiFeedbacks_SessionId",
                table: "AiFeedbacks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEvents_SessionId",
                table: "SessionEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEvents_SessionId_EventId",
                table: "SessionEvents",
                columns: new[] { "SessionId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiFeedbacks");

            migrationBuilder.DropTable(
                name: "SessionEvents");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
