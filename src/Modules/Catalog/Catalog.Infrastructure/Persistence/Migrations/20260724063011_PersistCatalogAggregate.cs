using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistCatalogAggregate : Migration
    {
        private static readonly string[] ProductIdAndIdColumns =
            new[] { "product_id", "id" };

        private static readonly string[] ProductIdAndDisplayOrderColumns =
            new[] { "product_id", "display_order" };

        private static readonly string[] ProductIdAndNameKeyColumns =
            new[] { "product_id", "name_key" };

        private static readonly string[] ProductIdAndOptionIdColumns =
            new[] { "product_id", "option_id" };

        private static readonly string[] ProductIdAndProductVariantIdColumns =
            new[] { "product_id", "product_variant_id" };

        private static readonly string[] ProductIdAndOptionCombinationKeyColumns =
            new[] { "product_id", "option_combination_key" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "catalog",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "product_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_options", x => x.id);
                    table.UniqueConstraint("ak_product_options_product_id_id", x => new { x.product_id, x.id });
                    table.CheckConstraint("ck_product_options_display_order", "display_order >= 0");
                    table.ForeignKey(
                        name: "fk_product_options_products",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    option_combination_key = table.Column<string>(type: "character(64)", nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discontinued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.UniqueConstraint("ak_product_variants_product_id_id", x => new { x.product_id, x.id });
                    table.CheckConstraint("ck_product_variants_lifecycle", "(\r\n    status = 'Draft'\r\n    AND activated_at_utc IS NULL\r\n    AND discontinued_at_utc IS NULL\r\n)\r\nOR\r\n(\r\n    status = 'Active'\r\n    AND activated_at_utc IS NOT NULL\r\n    AND discontinued_at_utc IS NULL\r\n)\r\nOR\r\n(\r\n    status = 'Discontinued'\r\n    AND discontinued_at_utc IS NOT NULL\r\n)");
                    table.CheckConstraint("ck_product_variants_status", "status IN ('Draft', 'Active', 'Discontinued')");
                    table.CheckConstraint("ck_product_variants_timestamp_order", "activated_at_utc IS NULL\r\nOR discontinued_at_utc IS NULL\r\nOR discontinued_at_utc >= activated_at_utc");
                    table.ForeignKey(
                        name: "fk_product_variants_products",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_options",
                schema: "catalog",
                columns: table => new
                {
                    product_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant_options", x => new { x.product_variant_id, x.option_id });
                    table.ForeignKey(
                        name: "fk_product_variant_options_option",
                        columns: x => new { x.product_id, x.option_id },
                        principalSchema: "catalog",
                        principalTable: "product_options",
                        principalColumns: ProductIdAndIdColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variant_options_variant",
                        columns: x => new { x.product_id, x.product_variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: ProductIdAndIdColumns,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_version",
                schema: "catalog",
                table: "products",
                sql: "version > 0");

            migrationBuilder.CreateIndex(
                name: "ux_product_options_product_display_order",
                schema: "catalog",
                table: "product_options",
                columns: ProductIdAndDisplayOrderColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_product_options_product_name",
                schema: "catalog",
                table: "product_options",
                columns: ProductIdAndNameKeyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variant_options_product_id_option_id",
                schema: "catalog",
                table: "product_variant_options",
                columns: ProductIdAndOptionIdColumns);

            migrationBuilder.CreateIndex(
                name: "IX_product_variant_options_product_id_product_variant_id",
                schema: "catalog",
                table: "product_variant_options",
                columns: ProductIdAndProductVariantIdColumns);

            migrationBuilder.CreateIndex(
                name: "ux_product_variants_product_combination_live",
                schema: "catalog",
                table: "product_variants",
                columns: ProductIdAndOptionCombinationKeyColumns,
                unique: true,
                filter: "status <> 'Discontinued'");

            migrationBuilder.CreateIndex(
                name: "ux_product_variants_sku",
                schema: "catalog",
                table: "product_variants",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variant_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "catalog");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_version",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "catalog",
                table: "products");
        }
    }
}
