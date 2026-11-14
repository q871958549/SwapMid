using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SystemManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.SystemManagement;

/// <summary>
/// <see cref="ICodeRuleRepository"/> 的 EFCore 实现（R1，蓝图 §20.3）：原子取号用单条参数化原生 SQL
/// <c>UPDATE ... RETURNING</c>，绕开 EF 变更跟踪与 LINQ 查询管道直接走 ADO.NET（<see cref="DbConnection"/> +
/// <see cref="DbCommand"/>），显式挂载 <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>
/// 到当前 ABP 工作单元的事务，保证与调用方 <c>requiresNew</c> 短事务同生共死。
///
/// SQL 兼容 PostgreSQL（生产）与 SQLite（本仓测试）双 provider：
/// ① 表/列名用双引号包裹的 PascalCase 标识符（两种 provider 均支持，migration 既有先例同款写法）；
/// ② 用 <c>"LastResetDate" IS NULL OR "LastResetDate" &lt;&gt; @key</c> 而非 PG 专属的 <c>IS DISTINCT FROM</c>；
/// ③ <c>WHERE "IsEnabled"</c> 直接判真值而非 <c>= true</c> 字面量，避开布尔字面量语法差异；
/// ④ <c>RETURNING</c> 子句 SQLite 需 3.35+（Microsoft.Data.Sqlite 满足），PG 原生支持。
///
/// 归零候选键（daily/monthly/yearly）由调用方传入的 <c>now</c> 在 C# 侧一次性算好三种格式，SQL 按行上
/// 实际 <see cref="CodeRule.ResetCycle"/>（枚举转 int 存储，见 <c>SystemManagementEntityTypeConfigurations</c>）
/// 挑选对应候选值——全程零额外查询，维持「单条 SQL」的原子性（若先查一次 ResetCycle 再拼 resetKey，
/// 等于退化回读改写两步，即便第二步仍是原子 UPDATE，也徒增一次往返且偏离蓝图「单条」的设计意图）。
/// </summary>
public class EfCoreCodeRuleRepository :
    EfCoreRepository<SwapMidDbContext, CodeRule, Guid>,
    ICodeRuleRepository
{
    // 周期枚举序号（SequenceResetCycle：Never=0/Daily=1/Monthly=2/Yearly=3）经参数传入 SQL，
    // 不在 SQL 文本里硬编码数字字面量——枚举定义若调整，本查询不会静默错配。
    private const string AdvanceSequenceSql = """
        UPDATE "AppCodeRules"
        SET
            "CurrentSequence" = CASE
                WHEN "ResetCycle" = @dailyCycle AND ("LastResetDate" IS NULL OR "LastResetDate" <> @dailyKey) THEN 1
                WHEN "ResetCycle" = @monthlyCycle AND ("LastResetDate" IS NULL OR "LastResetDate" <> @monthlyKey) THEN 1
                WHEN "ResetCycle" = @yearlyCycle AND ("LastResetDate" IS NULL OR "LastResetDate" <> @yearlyKey) THEN 1
                ELSE "CurrentSequence" + 1
            END,
            "LastResetDate" = CASE
                WHEN "ResetCycle" = @dailyCycle THEN @dailyKey
                WHEN "ResetCycle" = @monthlyCycle THEN @monthlyKey
                WHEN "ResetCycle" = @yearlyCycle THEN @yearlyKey
                ELSE "LastResetDate"
            END
        WHERE "EntityType" = @entityType AND "IsEnabled"
        RETURNING "Prefix", "Suffix", "IncludeDate", "DateFormat", "SequencePad", "CurrentSequence"
        """;

    public EfCoreCodeRuleRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<CodeRuleSequenceRow?> AdvanceSequenceAsync(string entityType, DateTime now, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = AdvanceSequenceSql;
        // 显式挂载当前 UoW 事务（ABP requiresNew + isTransactional 已在 DbContext 上开启事务）——
        // 绕过 EF 查询管道直连 ADO.NET 时不会自动继承，必须手动赋值，否则 provider 会因「连接已在
        // 事务中但命令未绑定事务」拒绝执行。
        if (dbContext.Database.CurrentTransaction != null)
        {
            command.Transaction = dbContext.Database.CurrentTransaction.GetDbTransaction();
        }

        AddParameter(command, "@entityType", entityType);
        AddParameter(command, "@dailyCycle", (int)SequenceResetCycle.Daily);
        AddParameter(command, "@monthlyCycle", (int)SequenceResetCycle.Monthly);
        AddParameter(command, "@yearlyCycle", (int)SequenceResetCycle.Yearly);
        AddParameter(command, "@dailyKey", now.ToString("yyyyMMdd"));
        AddParameter(command, "@monthlyKey", now.ToString("yyyyMM"));
        AddParameter(command, "@yearlyKey", now.ToString("yyyy"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // 规则不存在 / 未启用：0 行更新，调用方据此抛 CodeRuleNotFound。
            return null;
        }

        return new CodeRuleSequenceRow(
            Prefix: reader.GetString(reader.GetOrdinal("Prefix")),
            Suffix: ReadNullableString(reader, "Suffix"),
            IncludeDate: reader.GetBoolean(reader.GetOrdinal("IncludeDate")),
            DateFormat: ReadNullableString(reader, "DateFormat"),
            SequencePad: reader.GetInt32(reader.GetOrdinal("SequencePad")),
            CurrentSequence: reader.GetInt64(reader.GetOrdinal("CurrentSequence")));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string? ReadNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
