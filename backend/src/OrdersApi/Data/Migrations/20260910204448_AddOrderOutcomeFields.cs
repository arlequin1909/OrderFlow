using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrdersApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderOutcomeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "orders");
        }
    }
}
