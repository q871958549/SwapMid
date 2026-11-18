using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class MySqlBinlogCdcCaptureService
{
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex BinlogFilenameRegex();

    internal sealed record MySqlColumnMeta(string Name, string DataType, bool IsNullable);

    internal sealed record MySqlTableMetadata(
        string Database,
        string Table,
        List<string> PrimaryKeys,
        List<MySqlColumnMeta> Columns)
    {
        public string FullName => $"{Database}.{Table}";
    }

    internal sealed record MySqlBinlogFileInfo(string Filename, long FileSizeBytes);

    internal sealed record MySqlBinlogServerState(
        string? Version,
        bool LogBinEnabled,
        string? BinlogFormat,
        string? BinlogRowImage,
        string? ActiveFile,
        long? ActivePosition,
        List<MySqlBinlogFileInfo> BinaryLogs,
        int InvalidTables,
        bool CheckpointExpired,
        long? WalLagBytes,
        List<string> Diagnostics,
        Dictionary<string, MySqlTableMetadata> Tables);

    internal sealed record MySqlBinlogCheckpoint(string Filename, long Position)
    {
        public string Token => $"{Filename}:{Position.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static MySqlBinlogCheckpoint? TryParseCheckpoint(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var sep = trimmed.LastIndexOf(':');
        if (sep <= 0 || sep >= trimmed.Length - 1)
        {
            return null;
        }

        var file = trimmed[..sep].Trim();
        if (!BinlogFilenameRegex().IsMatch(file) || file.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        if (!long.TryParse(trimmed[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos) || pos < 4)
        {
            return null;
        }

        return new MySqlBinlogCheckpoint(file, pos);
    }

    internal static MySqlBinlogCheckpoint ValidateCheckpoint(string raw)
        => TryParseCheckpoint(raw)
           ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
               .WithData("Reason", $"MySQL Binlog 位点格式无效（{raw}），示例：binlog.000003:197（偏移须 >= 4）。");

    internal static long? ComputeBinlogLagBytes(
        IReadOnlyList<MySqlBinlogFileInfo> binaryLogs,
        string? storedFile,
        long? storedPos,
        string? activeFile,
        long? activePos,
        out bool checkpointFound)
    {
        checkpointFound = false;
        if (string.IsNullOrWhiteSpace(storedFile) || !storedPos.HasValue ||
            string.IsNullOrWhiteSpace(activeFile) || !activePos.HasValue ||
            binaryLogs.Count == 0)
        {
            return null;
        }

        var startIdx = -1;
        var endIdx = -1;
        for (var i = 0; i < binaryLogs.Count; i++)
        {
            if (string.Equals(binaryLogs[i].Filename, storedFile, StringComparison.Ordinal))
            {
                startIdx = i;
            }
            if (string.Equals(binaryLogs[i].Filename, activeFile, StringComparison.Ordinal))
            {
                endIdx = i;
            }
        }

        if (startIdx < 0)
        {
            return null;
        }

        checkpointFound = true;
        if (endIdx < 0 || startIdx > endIdx)
        {
            return 0;
        }

        if (startIdx == endIdx)
        {
            return Math.Max(0, activePos.Value - storedPos.Value);
        }

        long total = Math.Max(0, binaryLogs[startIdx].FileSizeBytes - storedPos.Value);
        for (var i = startIdx + 1; i < endIdx; i++)
        {
            total += Math.Max(0, binaryLogs[i].FileSizeBytes);
        }

        total += Math.Max(0, activePos.Value);
        return total;
    }

    internal static Dictionary<string, object?> MapRowCells(
        IReadOnlyList<object?> cells,
        bool[] columnsPresent,
        IReadOnlyList<MySqlColumnMeta> columns)
    {
        var dict = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
        var cellIdx = 0;
        var limit = Math.Min(columnsPresent.Length, columns.Count);
        for (var colIdx = 0; colIdx < limit; colIdx++)
        {
            if (!columnsPresent[colIdx])
            {
                continue;
            }

            var raw = cellIdx < cells.Count ? cells[cellIdx] : null;
            cellIdx++;
            dict[columns[colIdx].Name] = ConvertMySqlCellValue(raw, columns[colIdx].DataType);
        }

        return dict;
    }

    internal static object? ConvertMySqlCellValue(object? rawValue, string? dataType)
    {
        if (rawValue is null or DBNull)
        {
            return null;
        }

        if (rawValue is byte[] bytes)
        {
            if (IsTextualMySqlType(dataType))
            {
                return Encoding.UTF8.GetString(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        if (rawValue is DateTime dt)
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        if (rawValue is DateTimeOffset dto)
        {
            return dto.UtcDateTime;
        }

        return rawValue;
    }

    private static bool IsTextualMySqlType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return false;
        }

        return dataType.ToLowerInvariant() is
            "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext" or
            "json" or "enum" or "set";
    }

    private static IReadOnlyList<MonitoredTableSpec> ParseRequiredTables(string? raw, string? defaultDatabase)
    {
        var tables = CdcProtocolHelpers.ParseMonitoredTables(raw, defaultSchema: defaultDatabase ?? string.Empty);
        if (tables.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "MySQL Binlog 模式下必须配置至少 1 张监听表（如 orders 或 appdb.orders）。");
        }

        if (tables.Any(t => string.IsNullOrWhiteSpace(t.Schema)))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "MySQL 监听表缺少数据库前缀，请写为 db.table 或在数据源配置中指定默认 database。");
        }

        return tables;
    }

    private static async Task<MySqlBinlogServerState> InspectServerAsync(
        MySqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        string? storedCheckpoint,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        string? version = null;
        await using (var vCmd = new MySqlCommand("SELECT VERSION()", conn))
        {
            version = Convert.ToString(await vCmd.ExecuteScalarAsync(cancellationToken));
        }

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var varCmd = new MySqlCommand(
            "SHOW VARIABLES WHERE Variable_name IN ('log_bin', 'binlog_format', 'binlog_row_image')",
            conn))
        await using (var vr = await varCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await vr.ReadAsync(cancellationToken))
            {
                vars[vr.GetString(0)] = vr.GetString(1);
            }
        }

        var logBinOn = string.Equals(vars.GetValueOrDefault("log_bin"), "ON", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(vars.GetValueOrDefault("log_bin"), "1", StringComparison.OrdinalIgnoreCase);
        var format = vars.GetValueOrDefault("binlog_format");
        var rowImage = vars.GetValueOrDefault("binlog_row_image");

        if (!logBinOn)
        {
            diagnostics.Add("MySQL 源库未开启二进制日志（log_bin=OFF），无法运行 Binlog CDC。");
        }

        if (!string.Equals(format, "ROW", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"MySQL 源库 binlog_format 当前为 {format ?? "UNKNOWN"}，必须配置为 ROW。");
        }

        if (!string.IsNullOrWhiteSpace(rowImage) &&
            !string.Equals(rowImage, "FULL", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"建议将 MySQL binlog_row_image 设为 FULL（当前为 {rowImage}），以保留更新事件的完整前后行映像。");
        }

        var (activeFile, activePos) = await ReadActiveMasterPositionAsync(conn, cancellationToken);
        var logs = await ReadBinaryLogsAsync(conn, diagnostics, cancellationToken);

        var parsedCp = TryParseCheckpoint(storedCheckpoint);
        var checkpointExpired = false;
        var lagBytes = ComputeBinlogLagBytes(
            logs,
            parsedCp?.Filename,
            parsedCp?.Position,
            activeFile,
            activePos,
            out var checkpointFound);
        if (parsedCp != null && logs.Count > 0 && !checkpointFound)
        {
            checkpointExpired = true;
            diagnostics.Add(
                $"MySQL Binlog 检查点文件 {parsedCp.Filename} 已被源库过期清理（PURGE/expire_logs），请执行全量快照重建基线。");
        }

        var invalidTables = 0;
        var tableMap = new Dictionary<string, MySqlTableMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in tables)
        {
            var meta = await LoadTableMetadataAsync(conn, spec.Schema, spec.Table, cancellationToken);
            tableMap[spec.FullName] = meta;
            if (meta.Columns.Count == 0)
            {
                invalidTables++;
                diagnostics.Add($"源表 {spec.FullName} 不存在或无元数据读取权限。");
            }
            else if (meta.PrimaryKeys.Count == 0)
            {
                diagnostics.Add($"提示：表 {spec.FullName} 未声明主键（RelationalTable 镜像模式需手动配置 TargetSinkKeyColumns）。");
            }
        }

        return new MySqlBinlogServerState(
            version,
            logBinOn,
            format,
            rowImage,
            activeFile,
            activePos,
            logs,
            invalidTables,
            checkpointExpired,
            lagBytes,
            diagnostics,
            tableMap);
    }

    private static async Task<(string? File, long? Position)> ReadActiveMasterPositionAsync(
        MySqlConnection conn,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteBinaryLogStatusAsync(conn, "SHOW BINARY LOG STATUS", cancellationToken);
        }
        catch (MySqlException)
        {
            return await ExecuteBinaryLogStatusAsync(conn, "SHOW MASTER STATUS", cancellationToken);
        }
    }

    private static async Task<(string? File, long? Position)> ExecuteBinaryLogStatusAsync(
        MySqlConnection conn,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return (null, null);
        }

        var file = r.IsDBNull(0) ? null : r.GetString(0);
        long? pos = r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture);
        return (file, pos);
    }

    private static async Task<List<MySqlBinlogFileInfo>> ReadBinaryLogsAsync(
        MySqlConnection conn,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var logs = new List<MySqlBinlogFileInfo>();
        try
        {
            await using var cmd = new MySqlCommand("SHOW BINARY LOGS", conn);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var name = r.GetString(0);
                var size = r.FieldCount > 1 && !r.IsDBNull(1)
                    ? Convert.ToInt64(r.GetValue(1), CultureInfo.InvariantCulture)
                    : 0L;
                logs.Add(new MySqlBinlogFileInfo(name, size));
            }
        }
        catch (MySqlException)
        {
            diagnostics.Add("读取 SHOW BINARY LOGS 失败（需 REPLICATION CLIENT/BINLOG MONITOR 权限）。");
        }

        return logs;
    }

    private static async Task<MySqlTableMetadata> LoadTableMetadataAsync(
        MySqlConnection conn,
        string database,
        string table,
        CancellationToken cancellationToken)
    {
        var cols = new List<MySqlColumnMeta>();
        await using (var colCmd = new MySqlCommand("""
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """, conn))
        {
            colCmd.Parameters.AddWithValue("@schema", database);
            colCmd.Parameters.AddWithValue("@table", table);
            await using var r = await colCmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                cols.Add(new MySqlColumnMeta(
                    r.GetString(0),
                    r.GetString(1),
                    string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
            }
        }

        var pks = new List<string>();
        await using (var pkCmd = new MySqlCommand("""
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND CONSTRAINT_NAME = 'PRIMARY'
            ORDER BY ORDINAL_POSITION
            """, conn))
        {
            pkCmd.Parameters.AddWithValue("@schema", database);
            pkCmd.Parameters.AddWithValue("@table", table);
            await using var r = await pkCmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                pks.Add(r.GetString(0));
            }
        }

        return new MySqlTableMetadata(database, table, pks, cols);
    }

    private static CdcSchemaDriftEvaluation EvaluateSchemaFromTables(
        IReadOnlyDictionary<string, MySqlTableMetadata> tables,
        IReadOnlyList<MonitoredTableSpec> specs,
        string? baselineJson)
    {
        var snapshots = new List<CdcCapturedTableSchema>(specs.Count);
        foreach (var spec in specs)
        {
            if (!tables.TryGetValue(spec.FullName, out var meta) || meta.Columns.Count == 0)
            {
                continue;
            }

            var capturedCols = meta.Columns
                .Select(c => new CdcCapturedTableColumn(c.Name, c.DataType, c.IsNullable))
                .ToList();
            snapshots.Add(new CdcCapturedTableSchema(spec.Schema, spec.Table, capturedCols));
        }

        return CdcSchemaDriftEvaluator.Compare(baselineJson, snapshots);
    }
}
