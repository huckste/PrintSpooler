using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileSizeToJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "Jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "Jobs");
        }
    }
}
