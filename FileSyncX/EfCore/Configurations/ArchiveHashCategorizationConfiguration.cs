
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using FileSyncX.EfCore.Models;

namespace FileSyncX.Data.Configurations;

public sealed class ArchiveHashCategorizationConfiguration
    : IEntityTypeConfiguration<ArchiveHashCategorization>
{
    public void Configure(
        EntityTypeBuilder<ArchiveHashCategorization> builder)
    {
        // =====================================================
        // TABELA E CONSTRAINTS
        // =====================================================

        builder.ToTable(
            "tbl_archive_hash_categorization",
            "public",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_archive_sha256",
                    """
                    "sha256" IS NULL
                    OR "sha256" ~ '^[0-9a-fA-F]{64}$'
                    """);

                table.HasCheckConstraint(
                    "ck_archive_classification_status",
                    """
                    "classification_status" IN (
                        'pending',
                        'classified',
                        'review',
                        'unclassified'
                    )
                    """);
            });

        // =====================================================
        // PRIMARY KEY
        // =====================================================

        builder.HasKey(x => x.Id)
            .HasName("pk_tbl_archive_hash_categorization");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        // =====================================================
        // DOCUMENT UUID
        // =====================================================

        builder.Property(x => x.DocumentUuid)
            .HasColumnName("document_uuid")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()")
            .IsRequired();

        builder.HasAlternateKey(x => x.DocumentUuid)
            .HasName("uq_archive_document_uuid");

        // =====================================================
        // SHA256
        // =====================================================

        builder.Property(x => x.Sha256)
            .HasColumnName("sha256")
            .HasColumnType("character varying(64)")
            .HasMaxLength(64);

        // =====================================================
        // SOURCE FILE
        // =====================================================

        builder.Property(x => x.SourceFile)
            .HasColumnName("source_file")
            .HasColumnType("text")
            .IsRequired();

        // =====================================================
        // EMBEDDING
        // =====================================================

        builder.Property(x => x.Embedding)
            .HasColumnName("embedding")
            .HasColumnType("vector(1536)");

        // =====================================================
        // DESTINATION FILE
        // =====================================================

        builder.Property(x => x.DestinationFile)
            .HasColumnName("destination_file")
            .HasColumnType("text");

        // =====================================================
        // DESTINATION FOLDER
        // =====================================================

        builder.Property(x => x.DestinationFolder)
            .HasColumnName("destination_folder")
            .HasColumnType("text");

        // =====================================================
        // FOREIGN KEY ID HASH DEFAULT
        // =====================================================

        builder.Property(x => x.IdHashDefault)
            .HasColumnName("id_hash_default")
            .HasColumnType("integer");

        // =====================================================
        // CLASSIFICATION STATUS
        // =====================================================

        builder.Property(x => x.ClassificationStatus)
            .HasColumnName("classification_status")
            .HasColumnType("character varying(30)")
            .HasMaxLength(30)
            .HasDefaultValue("pending")
            .IsRequired();

        // =====================================================
        // SIMILARITY SCORE
        // =====================================================

        builder.Property(x => x.SimilarityScore)
            .HasColumnName("similarity_score")
            .HasColumnType("double precision");

        // =====================================================
        // CREATED AT
        // =====================================================

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // =====================================================
        // CLASSIFIED AT
        // =====================================================

        builder.Property(x => x.ClassifiedAt)
            .HasColumnName("classified_at")
            .HasColumnType("timestamp with time zone");

        // =====================================================
        // RELACIONAMENTO
        // =====================================================

        builder.HasOne(x => x.HashDefault)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.IdHashDefault)
            .HasConstraintName("fk_archive_hash_default")
            .OnDelete(DeleteBehavior.SetNull);

        // =====================================================
        // ÍNDICES RELACIONAIS
        // =====================================================

        builder.HasIndex(x => x.IdHashDefault)
            .HasDatabaseName("ix_archive_hash_default");

        builder.HasIndex(x => x.Sha256)
            .HasDatabaseName("ix_archive_sha256");

        // =====================================================
        // ÍNDICE VETORIAL HNSW
        // =====================================================

        builder.HasIndex(x => x.Embedding)
            .HasDatabaseName("ix_archive_embedding_hnsw")
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasFilter("\"embedding\" IS NOT NULL");
    }
}
