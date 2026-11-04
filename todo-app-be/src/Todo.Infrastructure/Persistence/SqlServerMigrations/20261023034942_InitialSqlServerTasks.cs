using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Infrastructure.Persistence.SqlServerMigrations
{
    /// <inheritdoc />
    public partial class InitialSqlServerTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.CheckConstraint("CK_Tasks_Completion", "([Status] = 'Completed' AND [CompletedAt] IS NOT NULL) OR ([Status] <> 'Completed' AND [CompletedAt] IS NULL)");
                    table.CheckConstraint("CK_Tasks_Dates", "[DueDate] IS NULL OR [StartDate] IS NULL OR [DueDate] >= [StartDate]");
                    table.CheckConstraint("CK_Tasks_Priority", "[Priority] IN ('Low', 'Normal', 'High', 'Urgent')");
                    table.CheckConstraint("CK_Tasks_Status", "[Status] IN ('Pending', 'InProgress', 'Completed')");
                    table.CheckConstraint("CK_Tasks_Title", "LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CreatedAt_Id",
                table: "Tasks",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status_DueDate",
                table: "Tasks",
                columns: new[] { "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
