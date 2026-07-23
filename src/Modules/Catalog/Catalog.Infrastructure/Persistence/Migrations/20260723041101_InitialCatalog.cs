using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discontinued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_lifecycle", "(\r\n    status = 'Draft'\r\n    AND published_at_utc IS NULL\r\n    AND discontinued_at_utc IS NULL\r\n)\r\nOR\r\n(\r\n    status = 'Published'\r\n    AND published_at_utc IS NOT NULL\r\n    AND discontinued_at_utc IS NULL\r\n)\r\nOR\r\n(\r\n    status = 'Discontinued'\r\n    AND discontinued_at_utc IS NOT NULL\r\n)");
                    table.CheckConstraint("ck_products_status", "status IN ('Draft', 'Published', 'Discontinued')");
                    table.CheckConstraint("ck_products_timestamp_order", "published_at_utc IS NULL\r\nOR discontinued_at_utc IS NULL\r\nOR discontinued_at_utc >= published_at_utc");
                });

            migrationBuilder.CreateIndex(
                name: "ux_products_slug",
                schema: "catalog",
                table: "products",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");
        }
    }
}
