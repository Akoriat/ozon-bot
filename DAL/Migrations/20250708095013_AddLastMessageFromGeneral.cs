using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddLastMessageFromGeneral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LastMessageIdFromGenerals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastMessageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LastMessageIdFromGenerals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewDataEntries_SourceRecordId",
                table: "NewDataEntries",
                column: "SourceRecordId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LastMessageIdFromGenerals");

            migrationBuilder.DropIndex(
                name: "IX_NewDataEntries_SourceRecordId",
                table: "NewDataEntries");
        }
    }
}
