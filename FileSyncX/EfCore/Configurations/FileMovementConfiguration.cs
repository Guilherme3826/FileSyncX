using FileSyncX.EfCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileSyncX.EfCore.Configurations;

public sealed class FileMovementConfiguration
    : IEntityTypeConfiguration<FileMovement>
{
    public void Configure(EntityTypeBuilder<FileMovement> builder)
    {
        // =====================================================
        // TABELA
        // =====================================================

        builder.ToTable("tbl_file_movement", "public");

        // =====================================================
        // PRIMARY KEY
        // =====================================================

        builder.HasKey(x => x.Id)
            .HasName("pk_tbl_file_movement");

        // IDENTITY
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        // =====================================================
        // FILE NAME
        // =====================================================

        builder.Property(x => x.FileName)
            .HasColumnName("file_name")
            .HasColumnType("text")
            .IsRequired();

        // =====================================================
        // SOURCE PATH
        // =====================================================

        builder.Property(x => x.SourcePath)
            .HasColumnName("source_path")
            .HasColumnType("text")
            .IsRequired();

        // =====================================================
        // DESTINATION PATH
        // =====================================================

        builder.Property(x => x.DestinationPath)
            .HasColumnName("destination_path")
            .HasColumnType("text")
            .IsRequired();

        // =====================================================
        // MOVED AT
        // =====================================================

        builder.Property(x => x.MovedAt)
            .HasColumnName("moved_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // =====================================================
        // FOREIGN KEY - HASH DEFAULT
        // =====================================================

        builder.Property(x => x.IdHashDefault)
            .HasColumnName("id_hash_default")
            .IsRequired(false);



        // =====================================================
        // INDEXES
        // =====================================================

        builder.HasIndex(x => x.IdHashDefault)
            .HasDatabaseName("ix_tbl_file_movement_id_hash_default");

        builder.HasIndex(x => x.MovedAt)
            .HasDatabaseName("ix_tbl_file_movement_moved_at");
    }
}