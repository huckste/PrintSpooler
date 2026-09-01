using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintSpooler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIppJobIdToJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IppJobId",
                table: "Jobs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IppJobId",
                table: "Jobs");
        }
    }
}
