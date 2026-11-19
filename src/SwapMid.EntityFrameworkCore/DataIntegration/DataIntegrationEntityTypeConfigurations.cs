using SwapMid.DataIntegration;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.DataIntegration;

public static class DataIntegrationEntityTypeConfigurations
{
    internal const string DurableExecutionIndexName =
        "IX_AppPipelineExecutions_Durable_StartedAt_Id";

    // 与 GetDurableStatesAsync 经 Npgsql 翻译后的枚举字面量 IN 谓词保持同形，确保 PostgreSQL
    // 能在规划期证明查询蕴含部分索引条件，而不依赖参数值推断。
    internal const string DurableExecutionIndexFilter =
        "\"Status\" IN (1, 5)";
    internal const string UnresolvedPipelineIndexName =
        "UX_AppPipelineExecutions_Unresolved_PipelineId";
    internal const string UnresolvedTargetResourceIndexName =
        "UX_AppPipelineExecutions_Unresolved_TargetResourceHash";
    internal const string UnresolvedVerifiedTargetResourceIndexName =
        "UX_AppPipelineExecutions_Unresolved_VerifiedTargetResourceHash";
    internal const string UnresolvedVerifiedTargetResourceIndexFilter =
        "\"Status\" IN (1, 5) AND \"VerifiedTargetResourceHash\" IS NOT NULL";

    // PostgreSQL 与 SQLite 都支持 length/replace。用受控字符消去链验证
    // 剩余长度为 0，比 upper(value)=value 更严格：后者会错放 64 个 Z/中文。
    internal static string BuildUpperHexCheck(string quotedColumn)
    {
        var remaining = quotedColumn;
        foreach (var character in "0123456789ABCDEF")
        {
            remaining = $"replace({remaining}, '{character}', '')";
        }

        return $"length({quotedColumn}) = 64 AND length({remaining}) = 0";
    }

    public static void ConfigureDataIntegration(this ModelBuilder builder)
    {
        builder.Entity<DataSource>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "DataSources",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(DataSourceConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(DataSourceConsts.MaxDescriptionLength);
            b.Property(x => x.Config).IsRequired().HasMaxLength(DataSourceConsts.MaxConfigLength);
            b.Property(x => x.Type).HasConversion<int>();
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<DataPipeline>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "DataPipelines",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(DataPipelineConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(DataPipelineConsts.MaxDescriptionLength);
            b.Property(x => x.TransformConfig).HasMaxLength(DataPipelineConsts.MaxTransformConfigLength);
            b.Property(x => x.ScheduleCron).HasMaxLength(DataPipelineConsts.MaxScheduleCronLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.LastRunStatus).HasConversion<int?>();
            b.HasIndex(x => x.Name).IsUnique();

            b.HasOne<DataSource>()
                .WithMany()
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            b.HasOne<DataSource>()
                .WithMany()
                .HasForeignKey(x => x.TargetId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        builder.Entity<PipelineExecution>(b =>
        {
            var targetResourceHashCheck =
                BuildUpperHexCheck("\"TargetResourceHash\"");
            var verifiedTargetResourceHashCheck =
                BuildUpperHexCheck("\"VerifiedTargetResourceHash\"");
            var executionContractHashCheck =
                BuildUpperHexCheck("\"ExecutionContractHash\"");
            var targetCoordinationIdentityHashCheck =
                BuildUpperHexCheck("\"TargetCoordinationIdentityHash\"");

            b.ToTable(SwapMidConsts.DbTablePrefix + "PipelineExecutions",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PipelineName).IsRequired().HasMaxLength(PipelineExecutionConsts.MaxPipelineNameLength);
            b.Property(x => x.ErrorMessage).HasMaxLength(PipelineExecutionConsts.MaxErrorMessageLength);
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.TriggerType).HasConversion<int>();
            b.Property(x => x.TargetResourceHash)
                .HasMaxLength(PipelineExecutionConsts.Sha256HexLength);
            b.Property(x => x.VerifiedTargetResourceHash)
                .HasMaxLength(PipelineExecutionConsts.Sha256HexLength);
            b.Property(x => x.ExecutionContractHash)
                .HasMaxLength(PipelineExecutionConsts.Sha256HexLength);
            b.Property(x => x.TargetCoordinationIdentityHash)
                .HasMaxLength(PipelineExecutionConsts.Sha256HexLength);

            b.HasOne<DataPipeline>()
                .WithMany()
                .HasForeignKey(x => x.PipelineId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.HasIndex(x => x.PipelineId);
            b.HasIndex(
                    x => x.PipelineId,
                    UnresolvedPipelineIndexName)
                .IsUnique()
                .HasFilter(DurableExecutionIndexFilter);
            b.HasIndex(
                    x => x.TargetResourceHash,
                    UnresolvedTargetResourceIndexName)
                .IsUnique()
                .HasFilter(DurableExecutionIndexFilter);
            b.HasIndex(
                    x => x.VerifiedTargetResourceHash,
                    UnresolvedVerifiedTargetResourceIndexName)
                .IsUnique()
                .HasFilter(UnresolvedVerifiedTargetResourceIndexFilter);
            b.HasIndex(x => x.StartedAt);
            b.HasIndex(
                    x => new { x.StartedAt, x.Id },
                    DurableExecutionIndexName)
                .HasFilter(DurableExecutionIndexFilter);

            b.ToTable(
                SwapMidConsts.DbTablePrefix +
                "PipelineExecutions",
                SwapMidConsts.DbSchema,
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_AppPipelineExecutions_EvidenceProtocol",
                        "\"EvidenceProtocol\" IN (0, 1)");
                    table.HasCheckConstraint(
                        "CK_AppPipelineExecutions_UnresolvedTargetResource",
                        "\"Status\" NOT IN (1, 5) OR " +
                        "(\"TargetResourceHash\" IS NOT NULL AND " +
                        targetResourceHashCheck + ")");
                    table.HasCheckConstraint(
                        "CK_AppPipelineExecutions_ReceiptEvidence",
                        "(\"EvidenceProtocol\" = 0 AND " +
                        "\"VerifiedTargetResourceHash\" IS NULL AND " +
                        "\"TargetResourceInstanceId\" IS NULL AND " +
                        "\"ExecutionContractHash\" IS NULL AND " +
                        "\"TargetCoordinationIdentityHash\" IS NULL) OR " +
                        "(\"EvidenceProtocol\" = 1 AND " +
                        "\"TargetResourceHash\" IS NOT NULL AND " +
                        targetResourceHashCheck + " AND " +
                        "\"VerifiedTargetResourceHash\" IS NOT NULL AND " +
                        verifiedTargetResourceHashCheck + " AND " +
                        "\"TargetResourceInstanceId\" IS NOT NULL AND " +
                        "\"TargetResourceInstanceId\" <> " +
                        "'00000000-0000-0000-0000-000000000000' AND " +
                        "\"ExecutionContractHash\" IS NOT NULL AND " +
                        executionContractHashCheck + " AND " +
                        "\"TargetCoordinationIdentityHash\" IS NOT NULL AND " +
                        targetCoordinationIdentityHashCheck + ")");
                });
        });

        builder.Entity<CdcStreamDefinition>(b =>
        {
            b.ToTable(
                SwapMidConsts.DbTablePrefix + "CdcStreams",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(CdcStreamConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(CdcStreamConsts.MaxDescriptionLength);
            b.Property(x => x.PublicationName).IsRequired().HasMaxLength(CdcStreamConsts.MaxPostgresIdentifierLength);
            b.Property(x => x.SlotName).IsRequired().HasMaxLength(CdcStreamConsts.MaxPostgresIdentifierLength);
            b.Property(x => x.MonitoredTables).HasMaxLength(CdcStreamConsts.MaxMonitoredTablesLength);
            b.Property(x => x.TargetTopicName).HasMaxLength(CdcStreamConsts.MaxTargetTopicNameLength);
            b.Property(x => x.TargetSinkDestination).HasMaxLength(CdcStreamConsts.MaxTargetSinkDestinationLength);
            b.Property(x => x.TargetSinkKeyColumns).HasMaxLength(CdcStreamConsts.MaxTargetSinkKeyColumnsLength);
            b.Property(x => x.TargetFieldMappingsJson).HasMaxLength(CdcStreamConsts.MaxTargetFieldMappingsJsonLength);
            b.Property(x => x.SoftDeleteColumn).HasMaxLength(CdcStreamConsts.MaxSoftDeleteColumnLength);
            b.Property(x => x.SoftDeleteTimestampColumn).HasMaxLength(CdcStreamConsts.MaxSoftDeleteTimestampColumnLength);
            b.Property(x => x.CatalogNamespace).HasMaxLength(CdcStreamConsts.MaxCatalogNamespaceLength);
            b.Property(x => x.CatalogTableName).HasMaxLength(CdcStreamConsts.MaxCatalogTableNameLength);
            b.Property(x => x.CatalogLastSyncStatus).HasMaxLength(CdcStreamConsts.MaxCatalogLastSyncStatusLength);
            b.Property(x => x.SchemaBaselineHash).HasMaxLength(CdcStreamConsts.MaxSchemaBaselineHashLength);
            b.Property(x => x.SchemaBaselineJson).HasMaxLength(CdcStreamConsts.MaxSchemaBaselineJsonLength);
            b.Property(x => x.SchemaDriftSummary).HasMaxLength(CdcStreamConsts.MaxSchemaDriftSummaryLength);
            b.Property(x => x.CheckpointLsn).HasMaxLength(CdcStreamConsts.MaxCheckpointLsnLength);
            b.Property(x => x.LastErrorCode).HasMaxLength(CdcStreamConsts.MaxErrorCodeLength);
            b.Property(x => x.LastErrorMessage).HasMaxLength(CdcStreamConsts.MaxErrorMessageLength);
            b.Property(x => x.RequiredColumns).HasMaxLength(CdcStreamConsts.MaxRequiredColumnsLength);
            b.Property(x => x.ContractViolationSummary).HasMaxLength(CdcStreamConsts.MaxContractViolationSummaryLength);
            b.Property(x => x.SourceType).HasConversion<int>();
            b.Property(x => x.TargetMode).HasConversion<int>();
            b.Property(x => x.TargetDeletePolicy).HasConversion<int>();
            b.Property(x => x.LakehouseTableFormat).HasConversion<int>();
            b.Property(x => x.LakehousePartitionStrategy).HasConversion<int>();
            b.Property(x => x.CatalogType).HasConversion<int>();
            b.Property(x => x.SchemaDriftPolicy).HasConversion<int>();
            b.Property(x => x.SnapshotStatus).HasConversion<int>();
            b.Property(x => x.Status).HasConversion<int>();
            b.HasIndex(x => x.Name).IsUnique();
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.SourceId);
            b.HasIndex(x => x.TargetSinkId);
            b.HasIndex(x => x.CatalogSinkId);

            b.HasOne<DataSource>()
                .WithMany()
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            b.HasOne<DataPipeline>()
                .WithMany()
                .HasForeignKey(x => x.TargetPipelineId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            b.HasOne<DataSource>()
                .WithMany()
                .HasForeignKey(x => x.TargetSinkId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            b.HasOne<DataSource>()
                .WithMany()
                .HasForeignKey(x => x.CatalogSinkId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        builder.Entity<CdcWebhookDeliveryRecord>(b =>
        {
            b.ToTable(
                SwapMidConsts.DbTablePrefix + "CdcWebhookDeliveries",
                SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DeliveryId).IsRequired().HasMaxLength(CdcWebhookDeliveryConsts.MaxDeliveryIdLength);
            b.Property(x => x.CheckpointLsn).HasMaxLength(CdcWebhookDeliveryConsts.MaxCheckpointLsnLength);
            b.Property(x => x.ErrorCode).HasMaxLength(CdcWebhookDeliveryConsts.MaxErrorCodeLength);
            b.Property(x => x.ErrorSummary).HasMaxLength(CdcWebhookDeliveryConsts.MaxErrorSummaryLength);
            b.Property(x => x.StoredPayloadJson).HasMaxLength(CdcWebhookDeliveryConsts.MaxStoredPayloadLength);
            b.HasIndex(x => new { x.StreamId, x.ReceivedAtUtc });
            b.HasOne<CdcStreamDefinition>()
                .WithMany()
                .HasForeignKey(x => x.StreamId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}
