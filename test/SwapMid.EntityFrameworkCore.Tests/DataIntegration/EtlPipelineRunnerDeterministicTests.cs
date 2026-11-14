using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Xunit;
using Xunit.Sdk;

namespace SwapMid.DataIntegration;

/// <summary>
/// 用完全可记录的 ADO.NET fake 锁定 reader 调用次数、批次边界及事务 token。
/// SQLite 集成测试验证真实事务；本组专门消除 provider 预取等非确定性。
/// </summary>
public class EtlPipelineRunnerDeterministicTests
{
    [Fact]
    public async Task 第50001次ReadAsync返回后立即拒绝_不发起第50002次且不开正式事务()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: EtlPipelineRunner.MaxRows + 1,
            maxReadCalls: EtlPipelineRunner.MaxRows + 1,
            requestCancellation.Token);
        var runner = CreateRunner(scenario);

        var exception = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            Transform(),
            requestCancellation.Token));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        scenario.Reader.ReadCalls.ShouldBe(EtlPipelineRunner.MaxRows + 1);
        scenario.Reader.ValueReadRows.ShouldBe(EtlPipelineRunner.MaxRows);
        scenario.Reader.AllReadTokensMatched.ShouldBeTrue();
        AssertStageVerificationPairs(
            scenario,
            Enumerable.Repeat(500, 99).ToArray());

        scenario.TargetTransaction.BeginCalls.ShouldBe(0);
        scenario.TargetTransaction.CommitCalls.ShouldBe(0);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
        scenario.TargetConnection.BeginTransactionTokens.ShouldBeEmpty();

        AssertExecutionTokenSemantics(
            scenario,
            requestCancellation.Token,
            expectsFormalTransaction: false);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: false);
    }

    [Fact]
    public async Task SqlServer两列1001行_暂存500_500_1且正式单批转存后提交()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1_001,
            maxReadCalls: 1_002,
            requestCancellation.Token);
        var runner = CreateRunner(scenario);

        var result = await runner.RunAsync(
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            Transform(),
            requestCancellation.Token);

        result.Rows.ShouldBe(1_001);
        result.TargetTruncated.ShouldBeTrue();
        scenario.Reader.ReadCalls.ShouldBe(1_002);
        scenario.Reader.ValueReadRows.ShouldBe(1_001);
        scenario.Reader.AllReadTokensMatched.ShouldBeTrue();

        var stagingInserts = scenario.TargetExecutions
            .Where(execution =>
                execution.Kind == RecordedCommandKind.StageInsert)
            .ToArray();
        stagingInserts.Select(execution => execution.LogicalRows)
            .ShouldBe(new[] { 500, 500, 1 });
        stagingInserts.Select(execution => execution.ParameterCount)
            .ShouldBe(new[] { 1_500, 1_500, 3 });
        stagingInserts.ShouldAllBe(execution =>
            execution.ParameterCount <= 2_000);
        stagingInserts.ShouldAllBe(execution =>
            execution.Transaction == null);
        AssertStageVerificationPairs(
            scenario,
            [500, 500, 1]);

        var targetGuard = scenario.TargetExecutions.Single(execution =>
            execution.Kind == RecordedCommandKind.TargetGuard);
        targetGuard.LogicalRows.ShouldBe(0);
        targetGuard.ParameterCount.ShouldBe(1);
        targetGuard.Transaction.ShouldBeSameAs(
            scenario.TargetTransaction);

        var targetTransfers = scenario.TargetExecutions
            .Where(execution =>
                execution.Kind == RecordedCommandKind.TransferToTarget)
            .ToArray();
        targetTransfers.Select(execution => execution.LogicalRows)
            .ShouldBe(new[] { 1_001 });
        targetTransfers.ShouldAllBe(execution =>
            execution.ParameterCount == 2);
        targetTransfers.ShouldAllBe(execution =>
            ReferenceEquals(execution.Transaction, scenario.TargetTransaction));

        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        AssertCommitConfirmationTokenSemantics(
            scenario,
            requestCancellation.Token);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);

        scenario.Timeline.IndexOf("reader.dispose")
            .ShouldBeLessThan(scenario.Timeline.IndexOf("target.begin"));

        AssertExecutionTokenSemantics(
            scenario,
            requestCancellation.Token,
            expectsFormalTransaction: true);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: true);
    }

    [Fact]
    public async Task Receipt必须在业务Dml前与共用目标事务且只提交一次()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 2,
            maxReadCalls: 3,
            requestCancellation.Token);
        var receiptStore = new RecordingReceiptStore(
            scenario.Timeline.Add);
        var context = CreateReceiptContext(EtlWriteMode.Truncate);
        var runner = CreateRunner(scenario, receiptStore: receiptStore);

        var result = await runner.RunAsync(
            context,
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            ReceiptTransform(EtlWriteMode.Truncate),
            requestCancellation.Token);

        result.Rows.ShouldBe(2);
        result.ReceiptEvidence.ShouldNotBeNull();
        receiptStore.Calls.ShouldBe(1);
        receiptStore.Transaction.ShouldBeSameAs(
            scenario.TargetTransaction);
        receiptStore.Evidence.ShouldBe(result.ReceiptEvidence);
        scenario.TargetExecutions
            .Where(execution => execution.Kind is
                RecordedCommandKind.DeleteTarget or
                RecordedCommandKind.TransferToTarget)
            .ShouldAllBe(execution =>
                ReferenceEquals(
                    execution.Transaction,
                    receiptStore.Transaction));
        scenario.Timeline.IndexOf("receipt.insert-or-read")
            .ShouldBeLessThan(scenario.Timeline.IndexOf("business.delete"));
        scenario.Timeline.IndexOf("receipt.insert-or-read")
            .ShouldBeLessThan(scenario.Timeline.IndexOf("business.transfer"));
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task 零行AppendReceipt仍必须执行仅回执事务并提交()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 0,
            maxReadCalls: 1,
            requestCancellation.Token);
        var receiptStore = new RecordingReceiptStore(
            scenario.Timeline.Add);
        var context = CreateReceiptContext(EtlWriteMode.Append);
        var runner = CreateRunner(scenario, receiptStore: receiptStore);

        var result = await runner.RunAsync(
            context,
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            ReceiptTransform(EtlWriteMode.Append),
            requestCancellation.Token);

        result.Rows.ShouldBe(0);
        result.TargetTruncated.ShouldBeFalse();
        result.ReceiptEvidence.ShouldNotBeNull();
        result.ReceiptEvidence!.ProcessedRows.ShouldBe(0);
        receiptStore.Calls.ShouldBe(1);
        receiptStore.Transaction.ShouldBeSameAs(
            scenario.TargetTransaction);
        scenario.TargetExecutions.ShouldNotContain(execution =>
            execution.Kind == RecordedCommandKind.DeleteTarget ||
            execution.Kind == RecordedCommandKind.TransferToTarget);
        scenario.TargetTransaction.BeginCalls.ShouldBe(1);
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(ReceiptFailureKind.Insert)]
    [InlineData(ReceiptFailureKind.Contract)]
    [InlineData(ReceiptFailureKind.ResourceInstance)]
    public async Task Receipt写入或契约身份失败必须回滚且不触碰业务表(
        ReceiptFailureKind failureKind)
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        var receiptStore = new RecordingReceiptStore(
            scenario.Timeline.Add)
        {
            Exception = failureKind == ReceiptFailureKind.Insert
                ? new TimeoutException("receipt write failed")
                : new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid),
        };
        var runner = CreateRunner(scenario, receiptStore: receiptStore);

        await Should.ThrowAsync<Exception>(() => runner.RunAsync(
            CreateReceiptContext(EtlWriteMode.Truncate),
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            ReceiptTransform(EtlWriteMode.Truncate),
            requestCancellation.Token));

        receiptStore.Calls.ShouldBe(1);
        scenario.TargetExecutions.ShouldNotContain(execution =>
            execution.Kind == RecordedCommandKind.DeleteTarget ||
            execution.Kind == RecordedCommandKind.TransferToTarget);
        scenario.TargetTransaction.CommitCalls.ShouldBe(0);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(EtlTargetReceiptInsertResult.ExistingExact)]
    [InlineData(EtlTargetReceiptInsertResult.ExistingMismatch)]
    public async Task ExistingReceipt必须转OutcomeUnknown且绝不触碰业务表(
        EtlTargetReceiptInsertResult receiptResult)
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        var receiptStore = new RecordingReceiptStore(
            scenario.Timeline.Add)
        {
            Result = receiptResult,
        };
        var runner = CreateRunner(scenario, receiptStore: receiptStore);

        var exception = await Should.ThrowAsync<
            EtlTargetCommitOutcomeUnknownException>(() => runner.RunAsync(
                CreateReceiptContext(EtlWriteMode.Truncate),
                CreateDataSource("source_db"),
                CreateDataSource("target_db"),
                ReceiptTransform(EtlWriteMode.Truncate),
                requestCancellation.Token));

        exception.ReceiptEvidence.ShouldNotBeNull(
            "exact/mismatch 都必须携带本次 prepared evidence，供平台持久化后逐字段核验");
        exception.ReceiptEvidence.ShouldBe(
            receiptStore.Evidence,
            "OutcomeUnknown 不得丢弃本次 schema/batch/rows 三元组");
        exception.ReceiptEvidence!.TargetSchemaHash
            .ShouldNotBeNullOrWhiteSpace();
        exception.ReceiptEvidence.BatchDigest
            .ShouldNotBeNullOrWhiteSpace();
        exception.ReceiptEvidence.ProcessedRows.ShouldBe(1);
        scenario.TargetExecutions.ShouldNotContain(execution =>
            execution.Kind == RecordedCommandKind.DeleteTarget ||
            execution.Kind == RecordedCommandKind.TransferToTarget);
        scenario.TargetTransaction.CommitCalls.ShouldBe(0);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Receipt竞态已升级OutcomeUnknown时回滚失败不得降级为普通失败()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        scenario.TargetTransaction.RollbackException =
            new TimeoutException("rollback acknowledgement lost");
        var receiptStore = new RecordingReceiptStore(
            scenario.Timeline.Add)
        {
            Exception = new EtlTargetCommitOutcomeUnknownException(),
        };
        var runner = CreateRunner(scenario, receiptStore: receiptStore);

        var exception = await Should.ThrowAsync<
            EtlTargetCommitOutcomeUnknownException>(() => runner.RunAsync(
                CreateReceiptContext(EtlWriteMode.Append),
                CreateDataSource("source_db"),
                CreateDataSource("target_db"),
                ReceiptTransform(EtlWriteMode.Append),
                requestCancellation.Token));

        exception.InnerException.ShouldBeNull();
        scenario.TargetTransaction.CommitCalls.ShouldBe(0);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(1);
        scenario.TargetTransaction.IsDisposed.ShouldBeFalse(
            "rollback ACK 未知时不得再由 transaction dispose 无界重试");
        scenario.TargetConnection.DisposeAsyncCalls.ShouldBe(1);
        scenario.TargetConnection.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task 目标Commit异常应升级为OutcomeUnknown且不得泄露Provider原文()
    {
        const string providerSecret = "P0_COMMIT_ACK_SECRET_SENTINEL";
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        scenario.TargetTransaction.CommitException =
            new TimeoutException($"commit ack lost;Password={providerSecret}");
        var runner = CreateRunner(scenario);

        var exception = await Should.ThrowAsync<
            EtlTargetCommitOutcomeUnknownException>(() => runner.RunAsync(
                CreateDataSource("source_db"),
                CreateDataSource("target_db"),
                Transform(),
                requestCancellation.Token));

        exception.Message.ShouldNotContain(providerSecret);
        exception.InnerException.ShouldBeNull();
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        AssertCommitConfirmationTokenSemantics(
            scenario,
            requestCancellation.Token);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(1);
        AssertExecutionTokenSemantics(
            scenario,
            requestCancellation.Token,
            expectsFormalTransaction: true);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: true);
    }

    [Fact]
    public async Task Commit未知且回滚失败时必须跳过事务Dispose并由外层销毁目标会话()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        scenario.TargetTransaction.CommitException =
            new TimeoutException("commit acknowledgement lost");
        scenario.TargetTransaction.RollbackException =
            new TimeoutException("rollback acknowledgement lost");
        var runner = CreateRunner(scenario);

        await Should.ThrowAsync<EtlTargetCommitOutcomeUnknownException>(
            () => runner.RunAsync(
                CreateDataSource("source_db"),
                CreateDataSource("target_db"),
                Transform(),
                requestCancellation.Token));

        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(1);
        scenario.TargetTransaction.IsDisposed.ShouldBeFalse(
            "回滚已失败时不得让 DbTransaction.DisposeAsync 再次发起无 token 回滚");
        scenario.TargetConnection.DisposeAsyncCalls.ShouldBe(1);
        scenario.TargetConnection.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Commit已确认后事务DisposeAsync异常不得把成功翻成失败()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        scenario.TargetTransaction.DisposeAsyncException =
            new InvalidOperationException(
                "transaction dispose failed after confirmed commit");
        var runner = CreateRunner(scenario);

        var result = await runner.RunAsync(
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            Transform(),
            requestCancellation.Token);

        result.Rows.ShouldBe(1);
        result.TargetTruncated.ShouldBeTrue();
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        AssertCommitConfirmationTokenSemantics(
            scenario,
            requestCancellation.Token);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
        scenario.TargetTransaction.IsDisposed.ShouldBeTrue();
        AssertExecutionTokenSemantics(
            scenario,
            requestCancellation.Token,
            expectsFormalTransaction: true);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: true);
    }

    [Fact]
    public async Task Commit已确认后目标连接DisposeAsync异常不得把成功翻成失败()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        scenario.TargetConnection.DisposeAsyncException =
            new InvalidOperationException(
                "target connection dispose failed after confirmed commit");
        var runner = CreateRunner(scenario);

        var result = await runner.RunAsync(
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            Transform(),
            requestCancellation.Token);

        result.Rows.ShouldBe(1);
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: true);
    }

    [Fact]
    public async Task Commit已确认后尾部日志异常不得把成功翻成失败()
    {
        using var requestCancellation = new CancellationTokenSource();
        var scenario = new EtlScenario(
            trueRows: 1,
            maxReadCalls: 2,
            requestCancellation.Token);
        var runner = CreateRunner(
            scenario,
            new ThrowingInformationLogger());

        var result = await runner.RunAsync(
            CreateDataSource("source_db"),
            CreateDataSource("target_db"),
            Transform(),
            requestCancellation.Token);

        result.Rows.ShouldBe(1);
        scenario.TargetTransaction.CommitCalls.ShouldBe(1);
        scenario.TargetTransaction.RollbackCalls.ShouldBe(0);
        AssertAllResourcesDisposed(
            scenario,
            expectsFormalTransaction: true);
    }

    private static EtlPipelineRunner CreateRunner(
        IEtlRelationalConnectionFactory connectionFactory,
        ILogger<EtlPipelineRunner>? logger = null,
        IEtlTargetReceiptStore? receiptStore = null)
        => new(
            logger ?? NullLogger<EtlPipelineRunner>.Instance,
            connectionFactory,
            receiptStore);

    private static DataSource CreateDataSource(string database)
        => new(
            Guid.NewGuid(),
            database,
            DataSourceType.RelationalDatabase,
            $$"""{"provider":"sqlserver","database":"{{database}}","encrypt":true,"trustServerCertificate":false}""");

    private static string Transform()
        => JsonSerializer.Serialize(new
        {
            sourceQuery = "SELECT id, value FROM source_rows",
            targetTable = "target_rows",
            mode = "truncate",
        });

    private static string ReceiptTransform(EtlWriteMode mode)
        => JsonSerializer.Serialize(new
        {
            sourceQuery = "SELECT id, value FROM source_rows",
            targetTable = "target_rows",
            mode = mode == EtlWriteMode.Append
                ? "append-receipt-v1"
                : "truncate-receipt-v1",
        });

    private static EtlExecutionContext CreateReceiptContext(
        EtlWriteMode mode)
    {
        const string sourceQuery =
            "SELECT id, value FROM source_rows";
        return new EtlExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', PipelineExecutionConsts.Sha256HexLength),
            new string('B', PipelineExecutionConsts.Sha256HexLength),
            new string('C', PipelineExecutionConsts.Sha256HexLength),
            new string('D', PipelineExecutionConsts.Sha256HexLength),
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(sourceQuery))),
            "target_rows",
            mode,
            new EtlTargetReceiptContext(Guid.NewGuid()));
    }

    private static void AssertExecutionTokenSemantics(
        EtlScenario scenario,
        CancellationToken requestToken,
        bool expectsFormalTransaction)
    {
        var executionToken =
            scenario.SourceConnection.OpenTokens.Single();
        executionToken.ShouldNotBe(requestToken);
        executionToken.CanBeCanceled.ShouldBeTrue();
        scenario.Reader.ExecutionToken.ShouldBe(executionToken);
        scenario.TargetConnection.OpenTokens.ShouldBe(
            new[] { executionToken });
        scenario.SourceCommand.ReaderTokens.ShouldBe(
            new[] { executionToken });
        scenario.SourceCommand.ReaderBehaviors.ShouldBe(
        [
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
        ]);
        scenario.TargetExecutions
            .Where(execution =>
                execution.Kind != RecordedCommandKind.DropStage)
            .ShouldAllBe(execution =>
            execution.Token == executionToken);
        scenario.TargetExecutions.Count(execution =>
            execution.Kind == RecordedCommandKind.CreateStage).ShouldBe(1);
        var drop = scenario.TargetExecutions.Single(execution =>
            execution.Kind == RecordedCommandKind.DropStage);
        drop.Token.ShouldNotBe(requestToken);
        drop.Token.ShouldNotBe(executionToken);
        drop.Token.CanBeCanceled.ShouldBeTrue();
        drop.Token.IsCancellationRequested.ShouldBeFalse();

        if (expectsFormalTransaction)
        {
            scenario.TargetConnection.BeginTransactionTokens
                .ShouldBe(new[] { executionToken });
            scenario.TargetTransaction.BeginCalls.ShouldBe(1);
            scenario.TargetTransaction.BegunIsolationLevel
                .ShouldBe(IsolationLevel.ReadCommitted);
            scenario.TargetExecutions
                .Where(execution =>
                    execution.Kind is
                        RecordedCommandKind.TargetGuard or
                        RecordedCommandKind.DeleteTarget or
                        RecordedCommandKind.TransferToTarget)
                .ShouldAllBe(execution =>
                    ReferenceEquals(
                        execution.Transaction,
                        scenario.TargetTransaction));
        }
        else
        {
            scenario.TargetConnection.BeginTransactionTokens.ShouldBeEmpty();
            scenario.TargetTransaction.BeginCalls.ShouldBe(0);
        }
    }

    private static void AssertAllResourcesDisposed(
        EtlScenario scenario,
        bool expectsFormalTransaction)
    {
        scenario.Reader.IsDisposed.ShouldBeTrue();
        scenario.SourceCommand.IsDisposed.ShouldBeTrue();
        scenario.SourceConnection.IsDisposed.ShouldBeTrue();
        scenario.TargetConnection.IsDisposed.ShouldBeTrue();
        scenario.TargetTransaction.IsDisposed
            .ShouldBe(expectsFormalTransaction);
        scenario.TargetConnection.CreatedCommands.Count.ShouldBeGreaterThan(0);
        scenario.TargetConnection.CreatedCommands
            .ShouldAllBe(command => command.IsDisposed);
    }

    private static void AssertStageVerificationPairs(
        EtlScenario scenario,
        IReadOnlyList<int> expectedBatchRows)
    {
        var executions = scenario.TargetExecutions;
        var insertIndexes = Enumerable.Range(0, executions.Count)
            .Where(index =>
                executions[index].Kind ==
                RecordedCommandKind.StageInsert)
            .ToArray();
        insertIndexes.Length.ShouldBe(expectedBatchRows.Count);
        executions.Count(execution =>
            execution.Kind == RecordedCommandKind.StageVerify)
            .ShouldBe(insertIndexes.Length);

        for (var batchIndex = 0;
             batchIndex < insertIndexes.Length;
             batchIndex++)
        {
            var insertIndex = insertIndexes[batchIndex];
            (insertIndex + 1).ShouldBeLessThan(executions.Count);
            var insert = executions[insertIndex];
            var verify = executions[insertIndex + 1];
            verify.Kind.ShouldBe(RecordedCommandKind.StageVerify);
            insert.LogicalRows.ShouldBe(expectedBatchRows[batchIndex]);
            verify.LogicalRows.ShouldBe(expectedBatchRows[batchIndex]);
            verify.ParameterCount.ShouldBe(2);
            insert.Token.ShouldBe(verify.Token);
            insert.Transaction.ShouldBeNull();
            verify.Transaction.ShouldBeNull();
        }
    }

    private static void AssertCommitConfirmationTokenSemantics(
        EtlScenario scenario,
        CancellationToken requestToken)
    {
        var commitToken = scenario.TargetTransaction.CommitTokens
            .ShouldHaveSingleItem();
        commitToken.CanBeCanceled.ShouldBeTrue();
        commitToken.ShouldNotBe(requestToken);
        commitToken.ShouldNotBe(
            scenario.SourceConnection.OpenTokens.Single(),
            "目标 COMMIT 不得继承请求取消或执行权丢失信号，只能受独立总期限约束");
    }

    public enum ReceiptFailureKind
    {
        Insert,
        Contract,
        ResourceInstance,
    }

    private sealed class RecordingReceiptStore : IEtlTargetReceiptStore
    {
        private readonly Action<string> _recordEvent;

        public RecordingReceiptStore(Action<string> recordEvent)
        {
            _recordEvent = recordEvent;
        }

        public EtlTargetReceiptInsertResult Result { get; set; } =
            EtlTargetReceiptInsertResult.Inserted;
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }
        public DbTransaction? Transaction { get; private set; }
        public EtlTargetReceiptEvidence? Evidence { get; private set; }

        public Task<EtlTargetReceiptActivation> ProbeActivationAsync(
            DataSource target,
            EtlTransformConfig transform,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EtlTargetReceiptEvidence?> FindAsync(
            DataSource target,
            EtlExecutionContext executionContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IEtlTargetReceiptReadFence> AcquireReadFenceAsync(
            DataSource target,
            EtlExecutionContext executionContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<EtlTargetReceiptInsertResult> InsertOrReadAsync(
            DbConnection connection,
            DbTransaction transaction,
            EtlTargetReceiptProviderContext providerContext,
            EtlTargetReceiptEvidence evidence,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Transaction = transaction;
            Evidence = evidence;
            _recordEvent("receipt.insert-or-read");
            return Exception != null
                ? Task.FromException<EtlTargetReceiptInsertResult>(
                    Exception)
                : Task.FromResult(Result);
        }
    }

    private sealed class ThrowingInformationLogger
        : ILogger<EtlPipelineRunner>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                throw new InvalidOperationException(
                    "logger failed after confirmed commit");
            }
        }
    }

    private sealed class EtlScenario : IEtlRelationalConnectionFactory
    {
        private readonly StagingReplayStore _stagingRows = new();

        public EtlScenario(
            int trueRows,
            int maxReadCalls,
            CancellationToken requestToken)
        {
            Reader = new ScriptedDataReader(
                trueRows,
                maxReadCalls,
                requestToken,
                recordEvent: RecordEvent);
            SourceConnection = RecordingDbConnection.ForSource(Reader);
            SourceCommand = SourceConnection.SourceCommand!;
            TargetTransaction = new RecordingDbTransaction(RecordEvent);
            TargetConnection = RecordingDbConnection.ForTarget(
                TargetTransaction,
                RecordTargetExecution);
        }

        public ScriptedDataReader Reader { get; }
        public RecordingDbConnection SourceConnection { get; }
        public RecordingDbCommand SourceCommand { get; }
        public RecordingDbConnection TargetConnection { get; }
        public RecordingDbTransaction TargetTransaction { get; }
        public List<CommandExecution> TargetExecutions { get; } = [];
        public List<string> Timeline { get; } = [];

        public DbConnection Create(string configJson, int connectTimeoutSeconds)
        {
            using var config = JsonDocument.Parse(configJson);
            return config.RootElement.GetProperty("database").GetString() switch
            {
                "source_db" => SourceConnection,
                "target_db" => TargetConnection,
                _ => throw new XunitException("测试配置引用了未声明的数据源。"),
            };
        }

        private RecordingCommandResult RecordTargetExecution(
            RecordingDbCommand command,
            CancellationToken token)
        {
            var (kind, logicalRows) = Classify(command);
            DbDataReader? resultReader = null;
            if (kind == RecordedCommandKind.StageInsert)
            {
                logicalRows = _stagingRows.CaptureInsert(command);
            }
            else if (kind == RecordedCommandKind.StageVerify)
            {
                (logicalRows, resultReader) =
                    _stagingRows.CreateRangeReader(command);
            }

            var execution = new CommandExecution(
                command.CommandText,
                command.Parameters.Count,
                token,
                command.Transaction,
                kind,
                logicalRows);
            TargetExecutions.Add(execution);
            if (kind == RecordedCommandKind.DeleteTarget)
            {
                RecordEvent("business.delete");
            }
            else if (kind == RecordedCommandKind.TransferToTarget)
            {
                RecordEvent("business.transfer");
            }
            return new RecordingCommandResult(
                logicalRows,
                resultReader);
        }

        private static (RecordedCommandKind Kind, int LogicalRows) Classify(
            RecordingDbCommand command)
        {
            if (command.CommandText.StartsWith(
                    "SELECT [etl_row_",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    ", [id], [value]",
                    StringComparison.Ordinal) &&
                command.CommandText.Contains(
                    "FROM [etl_stage_",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "WHERE [etl_row_",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "ORDER BY [etl_row_",
                    StringComparison.OrdinalIgnoreCase))
            {
                command.Transaction.ShouldBeNull();
                return (RecordedCommandKind.StageVerify, 0);
            }

            if (command.CommandText.StartsWith(
                    "SELECT COUNT(",
                    StringComparison.OrdinalIgnoreCase))
            {
                command.Transaction.ShouldNotBeNull();
                command.Parameters.Count.ShouldBe(1);
                Convert.ToInt32(
                        command.Parameters["boundedLimit"].Value)
                    .ShouldBeGreaterThan(0);
                return (RecordedCommandKind.TargetGuard, 0);
            }

            if (command.CommandText.StartsWith(
                    "CREATE TEMP TABLE",
                    StringComparison.OrdinalIgnoreCase))
            {
                command.Transaction.ShouldBeNull();
                return (RecordedCommandKind.CreateStage, 0);
            }

            if (command.CommandText.StartsWith(
                    "DROP TABLE IF EXISTS",
                    StringComparison.OrdinalIgnoreCase))
            {
                command.Transaction.ShouldBeNull();
                return (RecordedCommandKind.DropStage, 0);
            }

            if (command.CommandText.StartsWith(
                    "DELETE FROM",
                    StringComparison.OrdinalIgnoreCase))
            {
                command.Transaction.ShouldNotBeNull();
                return (RecordedCommandKind.DeleteTarget, 0);
            }

            if (command.CommandText.StartsWith(
                    "INSERT INTO",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (command.Transaction == null)
                {
                    return (RecordedCommandKind.StageInsert, 0);
                }

                if (command.Parameters.Count != 2 ||
                    !command.CommandText.Contains(
                        "SELECT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new XunitException(
                        "正式目标转存必须是带两个范围参数的 INSERT SELECT。");
                }

                var first = Convert.ToInt64(
                    command.Parameters["stageStartRowId"].Value);
                var end = Convert.ToInt64(
                    command.Parameters["stageEndRowId"].Value);
                return (
                    RecordedCommandKind.TransferToTarget,
                    checked((int)(end - first)));
            }

            throw new XunitException(
                $"目标 fake 收到未声明的 SQL：{command.CommandText}");
        }

        private void RecordEvent(string name)
        {
            Timeline.Add(name);
        }
    }
}
