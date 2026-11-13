using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.SqliteMigrations
{
    /// <inheritdoc />
    public partial class AddSeriesProgressSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series_progress",
                columns: table => new
                {
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StreamId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StreamLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Episode = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_progress", x => x.ImdbId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_series_progress_UpdatedAt",
                table: "series_progress",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_progress");
        }
    }
}
