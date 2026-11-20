using System;
using Jersey_ecommerce.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jersey_ecommerce.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261120120000_ManualBkashPayments")]
    public partial class ManualBkashPayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualSenderNumber", table: "Payments",
                type: "nvarchar(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ManualRecipientNumber", table: "Payments",
                type: "nvarchar(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "ManualClaimedPaidAt", table: "Payments",
                type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "ManualSubmittedAt", table: "Payments",
                type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "ManualReviewedAt", table: "Payments",
                type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ManualReviewNote", table: "Payments",
                type: "nvarchar(450)", maxLength: 450, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ManualReviewedByUserId", table: "Payments",
                type: "nvarchar(450)", maxLength: 450, nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ManualBkashTrxId",
                table: "Payments",
                column: "TransactionReference",
                unique: true,
                filter: "[Method] = 'Bkash' AND [TransactionReference] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ManualBkashTrxId", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualSenderNumber", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualRecipientNumber", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualClaimedPaidAt", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualSubmittedAt", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualReviewedAt", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualReviewNote", table: "Payments");
            migrationBuilder.DropColumn(name: "ManualReviewedByUserId", table: "Payments");
        }
    }
}
