using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// ETL 有界读取与目标原子性源码门禁。行为正确性由真 SQLite 测试证明；本门禁负责阻止
/// 后续“为了简化”重新引入 Dapper 全量缓冲，或遗漏目标 transaction 参数。
/// </summary>
public class EtlStreamingBoundaryGuardTests
{
    [Fact]
    public void Runner必须先校验再连库并使用SequentialAccess()
    {
        var runner = LoadRunnerClass();
        var invocations = runner.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();

        invocations
            .Where(call => MemberName(call) is "QueryAsync" or "ToList")
            .ShouldBeEmpty(
                "ETL 源结果不得恢复为 QueryAsync/ToList 全量物化");

        var validate = invocations.Single(call =>
            call.Expression.ToString() == "EtlSourceQueryGuard.Validate");
        var connectionCreates = invocations
            .Where(call => call.Expression.ToString() is
                "_connectionFactory.Create" or
                "_connectionFactory.CreateTarget")
            .ToList();
        connectionCreates.Count.ShouldBe(2);
        connectionCreates.ShouldAllBe(
            call => call.SpanStart > validate.SpanStart,
            "历史配置也必须在创建任何外部连接前重验");

        var providerSessions = invocations
            .Where(call =>
                call.Expression.ToString() ==
                "EtlProviderSession.ConfigureAsync")
            .OrderBy(call => call.SpanStart)
            .ToList();
        providerSessions.Count.ShouldBe(2);
        var beginSourceTransaction = invocations.Single(call =>
            call.Expression.ToString() ==
            "BeginSourceReadOnlyTransactionAsync");
        var targetPreflight = invocations.Single(call =>
            call.Expression.ToString() ==
            "EtlTargetPreflight.PrepareAsync");
        providerSessions[0].SpanStart.ShouldBeLessThan(
            beginSourceTransaction.SpanStart,
            "MySQL 源时区必须在事务/查询前固定为 UTC");
        providerSessions[1].SpanStart.ShouldBeLessThan(
            targetPreflight.SpanStart,
            "MySQL 目标时区必须在目录查询/事务前固定为 UTC");
        var providerSession = LoadSource("EtlProviderSession.cs");
        providerSession.ShouldContain("@@SESSION.time_zone");
        providerSession.ShouldContain("NO_BACKSLASH_ESCAPES");

        var executeReader = invocations.Single(call =>
            MemberName(call) == "ExecuteReaderAsync");
        var readerArguments = executeReader.ArgumentList.ToString();
        readerArguments.ShouldContain("CommandBehavior.SequentialAccess");
        readerArguments.ShouldContain("CommandBehavior.SingleResult");
    }

    [Fact]
    public void 源关闭后才可开启正式表事务且暂存写入不得误绑正式事务()
    {
        var runner = LoadRunnerClass();
        var atomicWriter = LoadPartialClass(
            "EtlTargetAtomicWriter",
            "EtlTargetAtomicWriter.cs",
            "EtlTargetAtomicWriter.MySqlLease.cs");
        var stagingWriter = LoadClass(
            "EtlTargetStagingWriter.cs",
            "EtlTargetStagingWriter");

        var runnerInvocations = runner.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();
        var disposeReader = runnerInvocations.Single(call =>
            call.Expression.ToString() == "reader.DisposeAsync");
        var applyTarget = runnerInvocations.Single(call =>
            call.Expression.ToString() == "EtlTargetAtomicWriter.ApplyAsync");
        disposeReader.SpanStart.ShouldBeLessThan(
            applyTarget.SpanStart,
            "必须先耗尽并关闭源 reader，才能修改正式目标表");

        var stagingInsertCalls = stagingWriter.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call =>
                call.Expression.ToString() == "ExecuteCountedInsertAsync")
            .ToList();
        stagingInsertCalls.Count.ShouldBe(2);
        stagingInsertCalls.ShouldContain(call =>
            call.ArgumentList.Arguments.Any(argument =>
                argument.NameColon != null &&
                argument.NameColon.Name.Identifier.ValueText == "transaction" &&
                argument.Expression.IsKind(
                    Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression)),
            "staging 批次必须在正式表事务外写入");
        stagingInsertCalls.ShouldContain(call =>
            call.ArgumentList.Arguments.Any(argument =>
                argument.Expression.ToString() == "targetTransaction"),
            "staging→正式表转存必须绑定正式表事务");
        stagingWriter.ToString().ShouldContain(
            "EtlStagedValueGuard.VerifyAsync");
        stagingWriter.ToString().ShouldContain(
            "CREATE TEMPORARY TABLE");
        stagingWriter.ToString().ShouldContain("LIKE {quotedTarget}");
        stagingWriter.ToString().ShouldNotContain(
            "ENGINE=InnoDB AS");

        var transactionCommands = new[] { atomicWriter, stagingWriter }
            .SelectMany(type => type.DescendantNodes())
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => creation.Type.ToString() == "CommandDefinition")
            .Where(creation =>
                creation.Ancestors()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(call => MemberName(call) is
                        "ExecuteAsync" or
                        "QuerySingleAsync" or
                        "QuerySingleOrDefaultAsync"))
            .Where(creation =>
                creation.ArgumentList != null &&
                creation.ArgumentList.Arguments.Any(
                    argument => argument.NameColon != null &&
                                argument.NameColon.Name.Identifier.ValueText == "transaction"))
            .ToList();

        transactionCommands.Count.ShouldBe(12);
        transactionCommands.Count(creation =>
                creation.ArgumentList != null &&
                creation.ArgumentList.Arguments.Any(
                    argument => argument.NameColon != null &&
                                argument.NameColon.Name.Identifier.ValueText == "transaction" &&
                                argument.Expression.ToString() == "targetTransaction"))
            .ShouldBe(
                10,
                "SQL Server/MySQL/PostgreSQL 目标写者租约、保护、空间上限查询与 DELETE 必须携带正式表 transaction");
        transactionCommands.Count(creation =>
                creation.ArgumentList != null &&
                creation.ArgumentList.Arguments.Any(
                    argument => argument.NameColon != null &&
                                argument.NameColon.Name.Identifier.ValueText == "transaction" &&
                                argument.Expression.ToString() == "transaction"))
            .ShouldBe(
                2,
                "SQL Server 与通用 counted insert 都必须透传调用方事务");

        var transactionInvocations = atomicWriter.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();
        transactionInvocations.ShouldContain(call =>
            call.Expression.ToString() == "connection.BeginTransactionAsync");
        transactionInvocations.ShouldContain(call =>
            call.Expression.ToString() == "targetTransaction.CommitAsync" &&
            call.ArgumentList.ToString().Contains("commitConfirmationToken"));
        transactionInvocations.ShouldNotContain(call =>
            call.Expression.ToString() == "targetTransaction.CommitAsync" &&
            call.ArgumentList.ToString().Contains("CancellationToken.None"),
            "目标 COMMIT 必须保留独立总期限，不能无限占住 ETL 容量槽");
        transactionInvocations.ShouldContain(call =>
            call.Expression.ToString() == "targetTransaction.RollbackAsync" &&
            call.ArgumentList.ToString().Contains("cleanup.Token"));
        var acquireGuard = transactionInvocations.Single(call =>
            call.Expression.ToString() == "AcquireTargetGuardAsync");
        var acquireWriterLease = transactionInvocations.Single(call =>
            call.Expression.ToString() ==
            "AcquireSqlServerWriterLeaseAsync");
        acquireWriterLease.SpanStart.ShouldBeLessThan(
            acquireGuard.SpanStart,
            "先串行化 ETL 写者，避免两个 S 表锁同时升级 SIX 的转换死锁");
        var revalidate = transactionInvocations.Single(call =>
            call.Expression.ToString() == "EtlTargetPreflight.RevalidateLockedAsync");
        acquireGuard.SpanStart.ShouldBeLessThan(
            revalidate.SpanStart,
            "必须先持有 DDL 防护，再在锁内二次复验目标能力");
        atomicWriter.ToString().ShouldContain(
            "expectedSchemaFingerprint");
        atomicWriter.ToString().ShouldContain(
            "lockedSchemaFingerprint");
        atomicWriter.ToString().ShouldContain(
            "StringComparison.Ordinal");
        atomicWriter.ToString().ShouldContain("sys.sp_getapplock");
        atomicWriter.ToString().ShouldContain("GET_LOCK(@resource, @timeoutSeconds)");
        atomicWriter.ToString().ShouldContain("RELEASE_LOCK(@resource)");
        atomicWriter.ToString().ShouldNotContain("MySqlConnection.ClearPool");
        atomicWriter.ToString().ShouldContain(
            "connectionCleanupState.RequirePhysicalSessionAbort()");
        atomicWriter.ToString().ShouldContain(
            "if (!rollbackFailed)");
        atomicWriter.ToString().ShouldContain("MySqlWriterLeaseTimeoutSeconds");
        atomicWriter.ToString().ShouldContain(
            "commandTimeout: timeoutSeconds + 1");
        atomicWriter.ToString().ShouldNotContain(
            "timeoutSeconds = commandTimeoutSeconds");
        atomicWriter.ToString().ShouldContain(
            "mySqlWriterLease = BuildMySqlWriterLeaseResource");
        atomicWriter.ToString().ShouldContain(
            "mySqlWriterLeaseConfirmed = true");
        atomicWriter.ToString().ShouldContain(
            "TABLOCK, HOLDLOCK");
        atomicWriter.ToString().ShouldNotContain(
            "TABLOCK, UPDLOCK");

        atomicWriter.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .ShouldContain(creation =>
                creation.Type.ToString() == "CancellationTokenSource" &&
                creation.ArgumentList != null &&
                creation.ArgumentList.ToString().Contains("cleanupTimeoutSeconds"),
                "回滚必须使用独立短时限清理 token");

        var runnerSource = LoadSource("EtlPipelineRunner.cs");
        runnerSource.ShouldContain("_connectionFactory.CreateTarget(");
        runnerSource.ShouldContain("new EtlTargetConnectionLease(");
        runnerSource.ShouldContain("targetCleanupState");
        runnerSource.ShouldNotContain(
            "await using var tgtConn = _connectionFactory.Create(");

        var connectionFactory = LoadSource(
            "RelationalDataSourceConnection.cs");
        connectionFactory.ShouldContain(
            "Pooling = !isolateMySqlPhysicalSession");
        connectionFactory.ShouldContain(
            "CancellationTimeout = isolateMySqlPhysicalSession ? -1 : 2");
        LoadSource("EtlTargetAtomicWriter.MySqlLease.cs").ShouldContain(
            "ResetConnectionAsync(cleanup.Token)");
    }

    [Fact]
    public void SqlServer正式表必须拒绝不可证明的trigger与RLS语义()
    {
        var preflight = LoadSource("EtlTargetPreflight.cs");

        preflight.ShouldContain("ev.type_desc = N'INSERT'");
        preflight.ShouldContain(
            "@truncate = 1 AND ev.type_desc = N'DELETE'");
        preflight.ShouldContain("sys.security_predicates");
        preflight.ShouldContain("pol.is_enabled = 1");
        preflight.ShouldContain("t.temporal_type = 0");
        preflight.ShouldContain("t.is_filetable = 0");
        preflight.ShouldContain("t.is_memory_optimized = 0");
        preflight.ShouldContain(
            "t.is_remote_data_archive_enabled = 0");
        preflight.ShouldContain("t.is_external = 0");
        preflight.ShouldContain("t.is_node = 0");
        preflight.ShouldContain("t.is_edge = 0");
        preflight.ShouldContain("t.ledger_type = 0");
        preflight.ShouldContain("capabilities.MajorVersion < 13");
        preflight.ShouldContain("p.PARTITION_NAME IS NOT NULL");
        preflight.ShouldContain("ft.INDEX_TYPE");
        preflight.ShouldContain("'FULLTEXT'");
        preflight.ShouldContain("'COMPRESSED'");
        preflight.ShouldContain("COUNT(DISTINCT s.INDEX_NAME)");
        preflight.ShouldContain("HAS_PERMS_BY_NAME");
        preflight.ShouldContain("DB_NAME()");
        preflight.ShouldContain(
            "EtlTargetColumnContract.ValidateAsync");
        preflight.ShouldNotContain("tr.is_instead_of_trigger");

        var targetSession = LoadSource("EtlTargetSession.cs");
        targetSession.ShouldContain("SET ANSI_WARNINGS ON");
        targetSession.ShouldContain("SET ARITHABORT ON");
        targetSession.ShouldContain("SET XACT_ABORT ON");
        targetSession.ShouldContain("SET NUMERIC_ROUNDABORT ON");
        targetSession.ShouldContain("SET NUMERIC_ROUNDABORT OFF");
        targetSession.ShouldContain(
            "ResolveCanonicalTargetTableAsync");
        targetSession.ShouldContain("OBJECT_ID(@targetTable");
        targetSession.ShouldContain("@@lower_case_table_names");
        targetSession.ShouldContain("BINARY TABLE_SCHEMA");
        var columnContract =
            LoadSource("EtlTargetColumnContract.cs");
        columnContract.ShouldContain("c.generated_always_type = 0");
        columnContract.ShouldContain("c.is_hidden = 0");
        columnContract.ShouldContain("c.is_column_set = 0");

        var fingerprint =
            LoadSource("EtlTargetSchemaFingerprint.cs");
        fingerprint.ShouldContain("t.object_id");
        fingerprint.ShouldContain("t.modify_date");
        fingerprint.ShouldContain("c.column_id");
        fingerprint.ShouldContain("dc.definition");
        fingerprint.ShouldContain("cc.definition");
        fingerprint.ShouldContain("GENERATION_EXPRESSION");
        fingerprint.ShouldContain("SHOW CREATE TABLE");
        fingerprint.ShouldContain(
            "EtlMySqlCreateDefinition.NormalizeForFingerprint");
        fingerprint.ShouldNotContain("Regex.Replace");
        fingerprint.ShouldContain("IncrementalHash");
        fingerprint.ShouldContain("BitConverter.GetBytes(bytes.Length)");

        var staging = LoadSource("EtlTargetStagingWriter.cs");
        staging.ShouldContain("NormalizeParameterValue");
        staging.ShouldContain("精确能力探针");
    }

    [Fact]
    public void Runner必须有整次截止时间且清理不得复用已取消token()
    {
        var runner = LoadSourceFamily("EtlPipelineRunner");

        runner.ShouldContain("totalExecutionDeadline.Token");
        runner.ShouldContain(
            "CancellationTokenSource.CreateLinkedTokenSource(");
        runner.ShouldContain(
            "TimeSpan.FromSeconds(TotalExecutionTimeoutSeconds)");
        runner.ShouldContain("var executionCt = executionCancellation.Token");
        runner.ShouldContain("reader.ReadAsync(executionCt)");
        runner.ShouldContain("IsolationLevel.Snapshot");
        runner.ShouldContain("CancellationTokenSource(");
        runner.ShouldContain("cleanup.Token");
    }

    private static ClassDeclarationSyntax LoadRunnerClass()
        => LoadClass("EtlPipelineRunner.cs", "EtlPipelineRunner");

    private static ClassDeclarationSyntax LoadClass(
        string fileName,
        string className)
    {
        var syntax = CSharpSyntaxTree.ParseText(
                LoadSource(fileName))
            .GetRoot();
        return syntax.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == className);
    }

    private static SyntaxNode LoadPartialClass(
        string className,
        params string[] fileNames)
        => SyntaxFactory.CompilationUnit()
            .AddMembers(fileNames
                .Select(fileName => LoadClass(fileName, className))
                .ToArray());

    private static string LoadSource(string fileName)
    {
        var root = LocateRepositoryRoot();
        var file = Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            fileName);
        File.Exists(file).ShouldBeTrue($"未找到 ETL 源码：{file}");
        return File.ReadAllText(file);
    }

    private static string LoadSourceFamily(string baseName)
    {
        var root = LocateRepositoryRoot();
        var directory = Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration");
        var files = Directory.EnumerateFiles(
                directory,
                $"{baseName}*.cs",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        files.ShouldNotBeEmpty($"未找到 ETL 源码族：{baseName}");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string? MemberName(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member
            ? member.Name.Identifier.ValueText
            : invocation.Expression is IdentifierNameSyntax identifier
                ? identifier.Identifier.ValueText
                : null;

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwapMid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未能定位 SwapMid.slnx");
    }
}
