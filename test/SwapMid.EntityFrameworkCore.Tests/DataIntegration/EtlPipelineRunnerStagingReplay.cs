using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using Xunit.Sdk;

namespace SwapMid.DataIntegration;

internal enum RecordedCommandKind
{
    CreateStage,
    StageInsert,
    StageVerify,
    TargetGuard,
    DeleteTarget,
    TransferToTarget,
    DropStage,
}

internal sealed record CommandExecution(
    string Sql,
    int ParameterCount,
    CancellationToken Token,
    DbTransaction? Transaction,
    RecordedCommandKind Kind,
    int LogicalRows);

internal sealed record RecordingCommandResult(
    int AffectedRows,
    DbDataReader? Reader = null);

/// <summary>
/// 记录 staging INSERT 的原始参数，并为随后严格匹配的范围回读构造同值 reader。
/// 这不是宽泛 SQL 模拟器：调用方必须先把命令识别为 StageInsert/StageVerify。
/// </summary>
internal sealed class StagingReplayStore
{
    private readonly SortedDictionary<long, object?[]> _rows = [];

    public int CaptureInsert(RecordingDbCommand command)
    {
        if (command.Parameters.Count % 3 != 0)
        {
            throw new XunitException(
                "staging 两列 INSERT 每行必须有 row-id+2 个业务参数。");
        }

        var rowCount = command.Parameters.Count / 3;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowId = Convert.ToInt64(
                command.Parameters[$"r{rowIndex}"].Value);
            if (!_rows.TryAdd(
                    rowId,
                    [
                        Normalize(command.Parameters[$"p{rowIndex}_0"].Value),
                        Normalize(command.Parameters[$"p{rowIndex}_1"].Value),
                    ]))
            {
                throw new XunitException(
                    $"staging fake 收到重复内部行号 {rowId}。");
            }
        }

        return rowCount;
    }

    public (int Rows, DbDataReader Reader) CreateRangeReader(
        RecordingDbCommand command)
    {
        if (command.Parameters.Count != 2 ||
            !command.Parameters.Contains("stageStartRowId") ||
            !command.Parameters.Contains("stageEndRowId"))
        {
            throw new XunitException(
                "staging 回读必须仅携带两个范围参数。");
        }

        var start = Convert.ToInt64(
            command.Parameters["stageStartRowId"].Value);
        var end = Convert.ToInt64(
            command.Parameters["stageEndRowId"].Value);
        if (end <= start)
        {
            throw new XunitException("staging 回读范围必须非空且递增。");
        }

        var table = new DataTable();
        table.Columns.Add("stage_row_id", typeof(long));
        table.Columns.Add("id", typeof(object));
        table.Columns.Add("value", typeof(object));
        for (var rowId = start; rowId < end; rowId++)
        {
            if (!_rows.TryGetValue(rowId, out var values))
            {
                throw new XunitException(
                    $"staging 回读请求了尚未写入的内部行号 {rowId}。");
            }

            table.Rows.Add(
                rowId,
                values[0] ?? DBNull.Value,
                values[1] ?? DBNull.Value);
        }

        return (
            checked((int)(end - start)),
            table.CreateDataReader());
    }

    private static object? Normalize(object? value)
        => value is null or DBNull ? null : value;
}
