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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdminNotificationReads");
            migrationBuilder.DropTable(name: "AdminNotifications");
        }
    }
}
