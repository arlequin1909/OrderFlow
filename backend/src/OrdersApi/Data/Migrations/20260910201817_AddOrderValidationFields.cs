using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrdersApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderValidationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClienteNombre",
                table: "orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EventPublishError",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EventPublished",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClienteNombre",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "EventPublishError",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "EventPublished",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "orders");
        }
    }
}
