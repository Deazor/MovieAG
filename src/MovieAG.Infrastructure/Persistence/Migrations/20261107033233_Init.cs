using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_titles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: true),
                    JapaneseName = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    EpisodesTotal = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PosterUrl = table.Column<string>(type: "text", nullable: true),
                    Studio = table.Column<string>(type: "text", nullable: true),
                    Genres = table.Column<List<string>>(type: "text[]", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cached_titles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watch_progress",
                columns: table => new
                {
                    TitleId = table.Column<int>(type: "integer", nullable: false),
                    LastEpisode = table.Column<int>(type: "integer", nullable: false),
                    WatchedEpisodes = table.Column<List<int>>(type: "integer[]", nullable: false),
                    DubKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DubName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
