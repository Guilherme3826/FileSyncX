using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace FileSyncX.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "tbl_hash_default",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "text", nullable: false),
                    destination_folder = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_hash_default", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tbl_archive_hash_categorization",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_uuid = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_file = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    destination_file = table.Column<string>(type: "text", nullable: true),
                    destination_folder = table.Column<string>(type: "text", nullable: true),
                    id_hash_default = table.Column<int>(type: "integer", nullable: true),
                    classification_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    similarity_score = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tbl_archive_hash_categorization", x => x.id);
                    table.UniqueConstraint("uq_archive_document_uuid", x => x.document_uuid);
                    table.CheckConstraint("ck_archive_classification_status", "\"classification_status\" IN (\r\n    'pending',\r\n    'classified',\r\n    'review',\r\n    'unclassified'\r\n)");
                    table.CheckConstraint("ck_archive_sha256", "\"sha256\" IS NULL\r\nOR \"sha256\" ~ '^[0-9a-fA-F]{64}$'");
                    table.ForeignKey(
                        name: "fk_archive_hash_default",
                        column: x => x.id_hash_default,
                        principalSchema: "public",
                        principalTable: "tbl_hash_default",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_embedding_hnsw",
                schema: "public",
                table: "tbl_archive_hash_categorization",
                column: "embedding",
                filter: "\"embedding\" IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_hash_default",
                schema: "public",
                table: "tbl_archive_hash_categorization",
                column: "id_hash_default");

            migrationBuilder.CreateIndex(
                name: "ix_archive_sha256",
                schema: "public",
                table: "tbl_archive_hash_categorization",
                column: "sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tbl_archive_hash_categorization",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tbl_hash_default",
                schema: "public");
        }
    }
}
