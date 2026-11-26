
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using FileSyncX.EfCore.Models;

namespace FileSyncX.EfCore.Configurations;

public sealed class HashDefaultConfiguration
    : IEntityTypeConfiguration<HashDefault>
{
    public void Configure(EntityTypeBuilder<HashDefault> builder)
    {
        // Tabela
        builder.ToTable("tbl_hash_default", "public");

        // Primary Key
        builder.HasKey(x => x.Id)
            .HasName("pk_tbl_hash_default");

        // IDENTITY
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        // DESCRIPTION
        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        // DESTINATION FOLDER
        builder.Property(x => x.DestinationFolder)
            .HasColumnName("destination_folder")
            .HasColumnType("text")
            .IsRequired();

        // EMBEDDING
        builder.Property(x => x.Embedding)
            .HasColumnName("embedding")
            .HasColumnType("vector(1536)")
            .IsRequired();

        // CREATED AT
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
