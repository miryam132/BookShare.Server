using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservedById",
                table: "Books",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_ReservedById",
                table: "Books",
                column: "ReservedById");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Books_Reservation",
                table: "Books",
                sql: "(\"Status\" = 'Reserved' AND \"ReservedById\" IS NOT NULL AND \"ReservedById\" <> \"OwnerId\") OR (\"Status\" <> 'Reserved' AND \"ReservedById\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Users_ReservedById",
                table: "Books",
                column: "ReservedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A reviewed rollback must not leave a Reserved book without its holder.
            migrationBuilder.Sql("UPDATE \"Books\" SET \"Status\" = 'Available', \"ReservedById\" = NULL, \"Version\" = gen_random_uuid() WHERE \"Status\" = 'Reserved'");
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Users_ReservedById",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_ReservedById",
                table: "Books");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Books_Reservation",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ReservedById",
                table: "Books");
        }
    }
}
