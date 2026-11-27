using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FileSyncX.Migrations
{
    /// <inheritdoc />
    public partial class Fisical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tbl_file_movement",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    source_path = table.Column<string>(type: "text", nullable: false),
                    destination_path = table.Column<string>(type: "text", nullable: false),
                    moved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    id_hash_default = table.Column<int>(type: "integer", nullable: true),
                    HashDefaultId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_file_movement", x => x.id);
                    table.ForeignKey(
                        name: "FK_tbl_file_movement_tbl_hash_default_HashDefaultId",
                        column: x => x.HashDefaultId,
                        principalSchema: "public",
                        principalTable: "tbl_hash_default",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "tbl_fisical_folders",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tbl_fisical_folders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tbl_file_movement_HashDefaultId",
                schema: "public",
                table: "tbl_file_movement",
                column: "HashDefaultId");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_file_movement_id_hash_default",
                schema: "public",
                table: "tbl_file_movement",
                column: "id_hash_default");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_file_movement_moved_at",
                schema: "public",
                table: "tbl_file_movement",
                column: "moved_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_file_movement",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tbl_fisical_folders",
                schema: "public");
        }
    }
}
