using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jersey_ecommerce.Migrations
{
    /// <inheritdoc />
    public partial class bkashpayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BkashPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GatewayPaymentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    GatewayTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BkashPaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BkashPaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BkashPaymentAttempts_GatewayPaymentId",
                table: "BkashPaymentAttempts",
                column: "GatewayPaymentId",
                unique: true,
                filter: "[GatewayPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BkashPaymentAttempts_Nonce",
                table: "BkashPaymentAttempts",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BkashPaymentAttempts_OrderId_CreatedAt",
                table: "BkashPaymentAttempts",
                columns: new[] { "OrderId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BkashPaymentAttempts");
        }
    }
}
