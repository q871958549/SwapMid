using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// staging 写入后的逐值回读对账。目标类型的隐式转换即使“影响行数正确”，
/// 只要发生截断、替换、舍入或时间精度丢失，也必须在正式表事务前失败关闭。
/// </summary>
internal static class EtlStagedValueGuard
{
    internal static async Task VerifyAsync(
        DbConnection connection,
        EtlTargetStagingWriter.Stage stage,
        long firstRowId,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> expectedRows,
        RelationalDataSourceConnection.Probe targetProbe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var quotedStage =
            RelationalDataSourceConnection.QuoteIdentifierPart(
                stage.Name,
                targetProbe);
        var quotedRowId =
            RelationalDataSourceConnection.QuoteIdentifierPart(
                stage.RowIdColumn,
                targetProbe);
        var quotedColumns = string.Join(
            ", ",
            columns.Select(column =>
                RelationalDataSourceConnection.QuoteIdentifierPart(
                    column,
                    targetProbe)));
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {quotedRowId}, {quotedColumns}
            FROM {quotedStage}
            WHERE {quotedRowId} >= @stageStartRowId
              AND {quotedRowId} < @stageEndRowId
            ORDER BY {quotedRowId}
            """;
        command.CommandTimeout = commandTimeoutSeconds;
        AddParameter(command, "stageStartRowId", firstRowId);
        AddParameter(
            command,
            "stageEndRowId",
            checked(firstRowId + expectedRows.Count));

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            ct);
        for (var rowIndex = 0;
             rowIndex < expectedRows.Count;
             rowIndex++)
        {
            if (!await reader.ReadAsync(ct) ||
                Convert.ToInt64(reader.GetValue(0)) !=
                checked(firstRowId + rowIndex))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            for (var columnIndex = 0;
                 columnIndex < columns.Count;
                 columnIndex++)
            {
                var ordinal = columnIndex + 1;
                var actual = await reader.IsDBNullAsync(ordinal, ct)
                    ? null
                    : reader.GetValue(ordinal);
                if (!AreEquivalent(
                        expectedRows[rowIndex][columnIndex],
                        actual))
                {
                    throw EtlPipelineRunner.InvalidInput();
                }
            }
        }

        if (await reader.ReadAsync(ct))
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    internal static bool AreEquivalent(
        object? expected,
        object? actual)
    {
        if (expected is DBNull)
        {
            expected = null;
        }
        if (actual is DBNull)
        {
            actual = null;
        }
        if (expected == null || actual == null)
        {
            return expected == null && actual == null;
        }

        if (expected is string expectedString &&
            actual is string actualString)
        {
            return string.Equals(
                expectedString,
                actualString,
                StringComparison.Ordinal);
        }
        if (expected is byte[] expectedBytes &&
            actual is byte[] actualBytes)
        {
            return expectedBytes.AsSpan().SequenceEqual(actualBytes);
        }
        if (TryGetExactDecimal(expected, out var expectedNumber) &&
            TryGetExactDecimal(actual, out var actualNumber))
        {
            return expectedNumber == actualNumber;
        }
        if (expected is float expectedFloat &&
            actual is float actualFloat)
        {
            return BitConverter.SingleToInt32Bits(expectedFloat) ==
                   BitConverter.SingleToInt32Bits(actualFloat);
        }
        if (expected is double expectedDouble &&
            actual is double actualDouble)
        {
            return BitConverter.DoubleToInt64Bits(expectedDouble) ==
                   BitConverter.DoubleToInt64Bits(actualDouble);
        }
        if (expected is DateTime expectedDateTime &&
            actual is DateTime actualDateTime)
        {
            return expectedDateTime.Ticks == actualDateTime.Ticks;
        }
        if (expected is DateOnly expectedDate &&
            actual is DateTime actualDateAsDateTime)
        {
            return expectedDate.ToDateTime(TimeOnly.MinValue).Ticks ==
                   actualDateAsDateTime.Ticks;
        }
        if (expected is DateTime expectedDateTimeAsDate &&
            actual is DateOnly actualDate)
        {
            return expectedDateTimeAsDate.Ticks ==
                   actualDate.ToDateTime(TimeOnly.MinValue).Ticks;
        }
        if (expected is TimeOnly expectedTime &&
            actual is TimeSpan actualTimeSpanFromTime)
        {
            return expectedTime.Ticks == actualTimeSpanFromTime.Ticks;
        }
        if (expected is TimeSpan expectedTimeSpanAsTime &&
            actual is TimeOnly actualTime)
        {
            return expectedTimeSpanAsTime.Ticks == actualTime.Ticks;
        }
        if (expected is DateTimeOffset expectedOffset &&
            actual is DateTime actualUtcDateTime &&
            expectedOffset.Offset == TimeSpan.Zero &&
            actualUtcDateTime.Kind == DateTimeKind.Utc)
        {
            return expectedOffset.UtcTicks == actualUtcDateTime.Ticks;
        }
        if (expected is DateTimeOffset expectedOffset2 &&
            actual is DateTimeOffset actualOffset)
        {
            return expectedOffset2.EqualsExact(actualOffset);
        }
        if (expected is TimeSpan expectedTimeSpan &&
            actual is TimeSpan actualTimeSpan)
        {
            return expectedTimeSpan.Ticks == actualTimeSpan.Ticks;
        }

        return expected.GetType() == actual.GetType() &&
               expected.Equals(actual);
    }

    private static bool TryGetExactDecimal(
        object value,
        out decimal number)
    {
        switch (value)
        {
            case sbyte typed:
                number = typed;
                return true;
            case byte typed:
                number = typed;
                return true;
            case short typed:
                number = typed;
                return true;
            case ushort typed:
                number = typed;
                return true;
            case int typed:
                number = typed;
                return true;
            case uint typed:
                number = typed;
                return true;
            case long typed:
                number = typed;
                return true;
            case ulong typed:
                number = typed;
                return true;
            case decimal typed:
                number = typed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        long value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
