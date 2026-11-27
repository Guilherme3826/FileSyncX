using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileSyncX.Migrations
{
    /// <inheritdoc />
    public partial class Nome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "public",
                table: "tbl_hash_default",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                schema: "public",
                table: "tbl_hash_default");
        }
    }
}
