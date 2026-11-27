using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FileSyncX.Migrations
{
    /// <inheritdoc />
    public partial class new5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultFolderClassificationId",
                schema: "public",
                table: "tbl_hash_default",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IdDefaultFolderClassification",
                schema: "public",
                table: "tbl_hash_default",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tbl_default_folders_classification",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    folder_name = table.Column<string>(type: "varchar(255)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_default_folders_classification", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tbl_hash_default_DefaultFolderClassificationId",
                schema: "public",
                table: "tbl_hash_default",
                column: "DefaultFolderClassificationId");

            migrationBuilder.CreateIndex(
                name: "uq_tbl_default_folders_classification_folder_name",
                schema: "public",
                table: "tbl_default_folders_classification",
                column: "folder_name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tbl_hash_default_tbl_default_folders_classification_Default~",
                schema: "public",
                table: "tbl_hash_default",
                column: "DefaultFolderClassificationId",
                principalSchema: "public",
                principalTable: "tbl_default_folders_classification",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tbl_hash_default_tbl_default_folders_classification_Default~",
                schema: "public",
                table: "tbl_hash_default");

            migrationBuilder.DropTable(
                name: "tbl_default_folders_classification",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_tbl_hash_default_DefaultFolderClassificationId",
                schema: "public",
                table: "tbl_hash_default");

            migrationBuilder.DropColumn(
                name: "DefaultFolderClassificationId",
                schema: "public",
                table: "tbl_hash_default");

            migrationBuilder.DropColumn(
                name: "IdDefaultFolderClassification",
                schema: "public",
                table: "tbl_hash_default");
        }
    }
}
