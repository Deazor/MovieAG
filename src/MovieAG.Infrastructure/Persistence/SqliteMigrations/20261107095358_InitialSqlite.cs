using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.SqliteMigrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_titles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalName = table.Column<string>(type: "TEXT", nullable: true),
                    JapaneseName = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodesTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    PosterUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Studio = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cached_titles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watch_progress",
                columns: table => new
                {
                    TitleId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastEpisode = table.Column<int>(type: "INTEGER", nullable: false),
                    WatchedEpisodes = table.Column<string>(type: "TEXT", nullable: false),
                    DubKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DubName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PositionSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watch_progress", x => x.TitleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_watch_progress_UpdatedAt",
                table: "watch_progress",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cached_titles");

            migrationBuilder.DropTable(
                name: "watch_progress");
        }
    }
}
