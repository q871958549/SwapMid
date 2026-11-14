using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace SwapMid.EntityFrameworkCore;

/// <summary>只验证测试库工厂；不创建业务宿主、不访问外部系统。</summary>
[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
[Trait("Category", "Slow")]
public class SqliteSchemaTemplateTests(ITestOutputHelper output)
{
    [Fact]
    public void 模板复制必须与原建表结果和外键设置一致()
    {
        var buildSchema = typeof(SqliteTestDatabaseTemplates)
            .GetMethod("BuildSchemaTemplate", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var warm = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        for (var index = 0; index < 3; index++)
        {
            var timer = Stopwatch.StartNew();
            using var original = (SqliteConnection)buildSchema.Invoke(null, null)!;
            var originalMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            using var clone = SqliteTestDatabaseTemplates.CloneSchemaOnly();
            var cloneMs = timer.Elapsed.TotalMilliseconds;
            const string schemaSql = "SELECT group_concat(sql, char(10)) FROM (SELECT sql FROM sqlite_master ORDER BY type, name)";
            Scalar(clone, schemaSql).ShouldBe(Scalar(original, schemaSql));
            Scalar(clone, "PRAGMA foreign_keys").ShouldBe(Scalar(original, "PRAGMA foreign_keys"));
            output.WriteLine($"SQLITE_SCHEMA_BENCH baseline_ms={originalMs:F1} clone_ms={cloneMs:F1}");
        }
    }

    [Fact]
    public void 每次复制必须保持完整结构且隔离数据和DDL()
    {
        using var first = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        using var second = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        var schema = Scalar(first, "SELECT group_concat(name, '|') FROM (SELECT name FROM sqlite_master WHERE type='table' ORDER BY name)");
        Scalar(second, "SELECT group_concat(name, '|') FROM (SELECT name FROM sqlite_master WHERE type='table' ORDER BY name)").ShouldBe(schema);
        Convert.ToInt32(Scalar(first, "SELECT count(*) FROM sqlite_master WHERE type='table'")).ShouldBeGreaterThan(25);

        Execute(first, "CREATE TABLE schema_clone_probe(id INTEGER); INSERT INTO schema_clone_probe VALUES (1)");
        Scalar(first, "SELECT count(*) FROM schema_clone_probe").ShouldBe("1");
        Scalar(second, "SELECT count(*) FROM sqlite_master WHERE name='schema_clone_probe'").ShouldBe("0");
        first.Dispose();
        using var third = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        Scalar(third, "SELECT count(*) FROM sqlite_master WHERE name='schema_clone_probe'").ShouldBe("0");
        Scalar(third, "SELECT group_concat(name, '|') FROM (SELECT name FROM sqlite_master WHERE type='table' ORDER BY name)").ShouldBe(schema);
    }

    [Fact]
    public async Task 并发复制不得共享测试连接或污染模板()
    {
        using var start = new Barrier(8);
        var counts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait(TimeSpan.FromSeconds(30)).ShouldBeTrue();
            using var connection = SqliteTestDatabaseTemplates.CloneSchemaOnly();
            Execute(connection, "CREATE TABLE schema_clone_probe(id INTEGER)");
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO schema_clone_probe VALUES ($id)";
            command.Parameters.AddWithValue("$id", index);
            command.ExecuteNonQuery();
            Scalar(connection, "SELECT id FROM schema_clone_probe").ShouldBe(index.ToString());
            return Convert.ToInt32(Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE type='table'"));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)));
        counts.Distinct().Count().ShouldBe(1);
        using var fresh = SqliteTestDatabaseTemplates.CloneSchemaOnly();
        Scalar(fresh, "SELECT count(*) FROM sqlite_master WHERE name='schema_clone_probe'").ShouldBe("0");
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
