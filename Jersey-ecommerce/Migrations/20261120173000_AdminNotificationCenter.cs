using System;
using Jersey_ecommerce.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jersey_ecommerce.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261120173000_AdminNotificationCenter")]
    public partial class AdminNotificationCenter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminNotificationReads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminNotificationId = table.Column<long>(type: "bigint", nullable: false),
                    AdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotificationReads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminNotificationReads_AdminNotifications_AdminNotificationId",
                        column: x => x.AdminNotificationId,
                        principalTable: "AdminNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_CreatedAt_Id",
                table: "AdminNotifications", columns: new[] { "CreatedAt", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_Category_CreatedAt",
                table: "AdminNotifications", columns: new[] { "Category", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotificationReads_AdminNotificationId",
                table: "AdminNotificationReads", column: "AdminNotificationId");
            migrationBuilder.CreateIndex(
                name: "IX_AdminNotificationReads_AdminUserId_AdminNotificationId",
                table: "AdminNotificationReads",
                columns: new[] { "AdminUserId", "AdminNotificationId" },
                unique: true);

            // Bring today's outstanding work into the new Admin inbox once,
            // without replaying years of completed historical events.
            migrationBuilder.Sql("""
                INSERT INTO [AdminNotifications]
                    ([Category], [EventType], [Severity], [Title], [Message], [TargetUrl], [CreatedAt])
                SELECT TOP (300) 'Payments', 'payment.submitted', 'Warning',
                    N'bKash payment needs verification',
                    CONCAT(N'Payment for order ', o.[OrderNumber], N' is awaiting independent bKash wallet verification.'),
                    CONCAT('/Admin/Orders/Details/', CONVERT(nvarchar(20), o.[Id]), '#payment-review'),
                    SYSUTCDATETIME()
                FROM [Payments] p
                INNER JOIN [Orders] o ON o.[Id] = p.[OrderId]
                WHERE p.[Method] = 'Bkash' AND p.[Status] = 'Submitted'
                  AND o.[Status] = 'Pending'
                ORDER BY p.[Id] DESC;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [AdminNotifications]
                    ([Category], [EventType], [Severity], [Title], [Message], [TargetUrl], [CreatedAt])
                SELECT TOP (200) 'Reviews', 'review.submitted', 'Warning',
                    N'Customer review awaiting moderation',
                    CONCAT(N'A ', CONVERT(nvarchar(5), r.[Rating]),
                           N'-star product review is waiting for approval.'),
                    CONCAT('/Admin/Reviews/Details/', CONVERT(nvarchar(20), r.[Id])),
                    SYSUTCDATETIME()
                FROM [Reviews] r
                WHERE r.[IsApproved] = 0 AND r.[IsRejected] = 0
                ORDER BY r.[Id] DESC;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [AdminNotifications]
                    ([Category], [EventType], [Severity], [Title], [Message], [TargetUrl], [CreatedAt])
                SELECT TOP (200) 'Inventory',
                    CASE WHEN v.[StockQuantity] = 0 THEN 'stock.out' ELSE 'stock.low' END,
                    CASE WHEN v.[StockQuantity] = 0 THEN 'Critical' ELSE 'Warning' END,
                    CASE WHEN v.[StockQuantity] = 0 THEN
                        CONCAT(N'Out of stock: ', LEFT(v.[SKU], 125)) ELSE
                        CONCAT(N'Low stock: ', LEFT(v.[SKU], 125)) END,
                    CONCAT(N'Variant ', LEFT(v.[SKU], 100), N' has ',
                           CONVERT(nvarchar(20), v.[StockQuantity]), N' units available.'),
                    CONCAT('/Admin/Products/Edit/', CONVERT(nvarchar(20), v.[ProductId])),
                    SYSUTCDATETIME()
                FROM [ProductVariants] v
                INNER JOIN [Products] p ON p.[Id] = v.[ProductId]
                WHERE v.[IsActive] = 1 AND p.[IsPublished] = 1
                  AND v.[StockQuantity] BETWEEN 0 AND 5
                ORDER BY v.[Id] DESC;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdminNotificationReads");
            migrationBuilder.DropTable(name: "AdminNotifications");
        }
    }
}
