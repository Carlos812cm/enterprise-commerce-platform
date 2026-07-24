using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantOptionRecordConfiguration :
    IEntityTypeConfiguration<ProductVariantOptionRecord>
{
    public void Configure(
        EntityTypeBuilder<ProductVariantOptionRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "product_variant_options",
            CatalogDbContext.Schema);

        builder.HasKey(
                selection => new
                {
                    selection.ProductVariantId,
                    selection.OptionId
                })
            .HasName(
                "pk_product_variant_options");

        builder.Property(
                selection => selection.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid");

        builder.Property(
                selection =>
                    selection.ProductVariantId)
            .HasColumnName(
                "product_variant_id")
            .HasColumnType("uuid");

        builder.Property(
                selection => selection.OptionId)
            .HasColumnName("option_id")
            .HasColumnType("uuid");

        builder.Property(
                selection => selection.Value)
            .HasColumnName("value")
            .HasMaxLength(
                OptionValue.MaximumLength)
            .IsRequired();

        builder.HasOne<ProductVariantRecord>()
            .WithMany(
                variant =>
                    variant.OptionSelections)
            .HasForeignKey(
                selection => new
                {
                    selection.ProductId,
                    selection.ProductVariantId
                })
            .HasPrincipalKey(
                variant => new
                {
                    variant.ProductId,
                    variant.Id
                })
            .OnDelete(
                DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_product_variant_options_variant");

        builder.HasOne<ProductOptionRecord>()
            .WithMany()
            .HasForeignKey(
                selection => new
                {
                    selection.ProductId,
                    selection.OptionId
                })
            .HasPrincipalKey(
                option => new
                {
                    option.ProductId,
                    option.Id
                })
            .OnDelete(
                DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_product_variant_options_option");
    }
}
