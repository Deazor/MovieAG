using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "watch_progress",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PositionSeconds",
                table: "watch_progress",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "watch_progress");

            migrationBuilder.DropColumn(
                name: "PositionSeconds",
                table: "watch_progress");
        }
    }
}
