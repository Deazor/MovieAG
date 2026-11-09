using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovieProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movie_progress",
                columns: table => new
                {
                    ImdbId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PosterUrl = table.Column<string>(type: "text", nullable: true),
                    SourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StreamId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PositionSeconds = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movie_progress", x => x.ImdbId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_movie_progress_UpdatedAt",
                table: "movie_progress",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movie_progress");
        }
    }
}
