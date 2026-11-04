using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Infrastructure.Persistence.SqlServerMigrations
{
    /// <inheritdoc />
    public partial class AddPurposes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurposeId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Purposes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purposes", x => x.Id);
                    table.CheckConstraint("CK_Purposes_Title", "LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PurposeId",
                table: "Tasks",
                column: "PurposeId");

            migrationBuilder.CreateIndex(
                name: "IX_Purposes_Title_Id",
                table: "Purposes",
                columns: new[] { "Title", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Purposes_PurposeId",
                table: "Tasks",
                column: "PurposeId",
                principalTable: "Purposes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Purposes_PurposeId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "Purposes");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_PurposeId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PurposeId",
                table: "Tasks");
        }
    }
}
