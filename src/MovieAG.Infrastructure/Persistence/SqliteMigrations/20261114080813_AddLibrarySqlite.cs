using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieAG.Infrastructure.Persistence.SqliteMigrations
{
    /// <inheritdoc />
    public partial class AddLibrarySqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Favorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library", x => new { x.Kind, x.ItemId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_Favorite_UpdatedAt",
                table: "library",
                columns: new[] { "Favorite", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_library_Status_UpdatedAt",
                table: "library",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library");
        }
    }
}
