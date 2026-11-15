using System;
using System.Linq;
using SwapMid.EntityFrameworkCore;
using SwapMid.EntityFrameworkCore.DataIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 未决执行有界扫描的 PostgreSQL 契约：运行时模型、ModelSnapshot、迁移和查询形状
/// 必须共同维持同一部分索引语义，避免小规模测试通过而生产退化为全表排序扫描。
/// </summary>
public sealed class PipelineExecutionDurableIndexContractTests
{
    private const string IndexName =
        "IX_AppPipelineExecutions_Durable_StartedAt_Id";
    private static readonly string IndexFilter =
        $"\"Status\" IN ({(int)ExecutionStatus.Running}, " +
        $"{(int)ExecutionStatus.OutcomeUnknown})";

    [Fact]
    public void Durable_State_Partial_Index_Model_And_Migration_Up_Down_Must_Match()
    {
        using var dbContext = CreatePostgreSqlContext();
        DataIntegrationEntityTypeConfigurations.DurableExecutionIndexName
            .ShouldBe(IndexName);
        DataIntegrationEntityTypeConfigurations.DurableExecutionIndexFilter
            .ShouldBe(IndexFilter);
        AssertDurableIndex(dbContext.Model, "运行时模型");

        var migrationsAssembly = dbContext.GetService<IMigrationsAssembly>();
        migrationsAssembly.ModelSnapshot.ShouldNotBeNull();
        AssertDurableIndex(
            migrationsAssembly.ModelSnapshot!.Model,
            "SwapMidDbContextModelSnapshot");

        var migrations = dbContext.Database.GetMigrations().ToList();
        migrations.ShouldNotBeEmpty();
        var initialMigrationId = migrations.First();
        var upScript = dbContext.GetService<IMigrator>().GenerateScript(null, initialMigrationId);
        upScript.ShouldContain($"CREATE INDEX \"{IndexName}\"");
        upScript.ShouldContain(
            "ON \"AppPipelineExecutions\" (\"StartedAt\", \"Id\")");
        upScript.ShouldContain($"WHERE {IndexFilter};");
        upScript.ShouldContain(
            "INSERT INTO \"__EFMigrationsHistory\"");
        upScript.ShouldContain(
            $"VALUES ('{initialMigrationId}'");

        if (migrations.Count > 1)
        {
            var latestScript = dbContext.GetService<IMigrator>().GenerateScript(null, migrations[^1]);
            latestScript.ShouldContain($"CREATE INDEX \"{IndexName}\"");
            latestScript.ShouldNotContain($"DROP INDEX IF EXISTS \"{IndexName}\"");
        }
    }

    [Fact]
    public void Durable_State_Query_Must_Translate_To_Literal_Predicate_Total_Order_And_Bounded_Limit()
    {
        using var dbContext = CreatePostgreSqlContext();
        const int maxResultCount = 1025;

        var sql = EfCorePipelineExecutionRepository.BuildDurableStatesQuery(
                dbContext.Set<PipelineExecution>(),
                maxResultCount)
            .ToQueryString();
        var whereStart = sql.IndexOf("WHERE", StringComparison.Ordinal);
        var orderStart = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var limitStart = sql.IndexOf("LIMIT", StringComparison.Ordinal);
        whereStart.ShouldBeGreaterThanOrEqualTo(0);
        orderStart.ShouldBeGreaterThan(whereStart);
        limitStart.ShouldBeGreaterThan(orderStart);

        var predicate = sql[whereStart..orderStart];
        predicate.ShouldContain($"\"Status\" IN (" +
                                $"{(int)ExecutionStatus.Running}, " +
                                $"{(int)ExecutionStatus.OutcomeUnknown})");
        predicate.Contains('@').ShouldBeFalse(
            "部分索引谓词必须在规划期保持字面量，不能参数化状态集合");

        var orderBy = sql[orderStart..limitStart];
        var startedAtOrder = orderBy.IndexOf(
            "\"StartedAt\"",
            StringComparison.Ordinal);
        var idOrder = orderBy.IndexOf(
            "\"Id\"",
            StringComparison.Ordinal);
        startedAtOrder.ShouldBeGreaterThanOrEqualTo(0);
        idOrder.ShouldBeGreaterThan(startedAtOrder,
            "同 StartedAt 行必须由 Id 提供稳定末级全序");
        orderBy.ShouldNotContain("DESC");

        var limit = sql[limitStart..];
        limit.ShouldContain("LIMIT");
        limit.ShouldNotContain("ALL");
        sql.Contains(
                maxResultCount.ToString(),
                StringComparison.Ordinal)
            .ShouldBeTrue(
                "Take 上限必须进入 Npgsql SQL，禁止退化为无界拉取");
    }

    private static void AssertDurableIndex(IModel model, string modelName)
    {
        var entityType = model.FindEntityType(typeof(PipelineExecution));
        entityType.ShouldNotBeNull($"{modelName} 缺少 PipelineExecution");
        var index = entityType!.GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() ==
                DataIntegrationEntityTypeConfigurations
                    .DurableExecutionIndexName);
        index.ShouldNotBeNull($"{modelName} 缺少未决执行部分索引");
        index!.IsUnique.ShouldBeFalse();
        index.Properties.Select(property => property.Name).ShouldBe(
            new[]
            {
                nameof(PipelineExecution.StartedAt),
                nameof(PipelineExecution.Id),
            });
        index.GetFilter().ShouldBe(
            IndexFilter);
        entityType.GetIndexes().Any(candidate =>
                candidate.GetDatabaseName() !=
                DataIntegrationEntityTypeConfigurations
                    .DurableExecutionIndexName &&
                candidate.Properties.Select(property => property.Name)
                    .SequenceEqual(new[]
                    {
                        nameof(PipelineExecution.StartedAt),
                    }))
            .ShouldBeTrue($"{modelName} 不得删除既有 StartedAt 全量索引");
    }

    private static SwapMidDbContext CreatePostgreSqlContext()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        SwapMidEfCoreEntityExtensionMappings.Configure();
        var options = new DbContextOptionsBuilder<SwapMidDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=not_used;Username=not_used;Password=not_used",
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory"))
            .Options;
        return new SwapMidDbContext(options);
    }
}
