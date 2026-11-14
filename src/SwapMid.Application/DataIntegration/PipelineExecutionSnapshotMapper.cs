using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace SwapMid.DataIntegration;

/// <summary>
/// 把执行时保存的无凭据源/目标与写入语义快照投影到 DTO。历史记录没有快照时保持 null，禁止回查当前
/// DataSource 冒充历史事实。
/// </summary>
internal static class PipelineExecutionSnapshotMapper
{
    public static void Populate(
        PipelineExecution execution,
        PipelineExecutionDto dto)
    {
        var targetId = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetDataSourceIdPropertyName,
            32);
        dto.TargetDataSourceId = Guid.TryParseExact(
            targetId,
            "N",
            out var parsedTargetId)
            ? parsedTargetId
            : null;
        dto.TargetDataSourceName = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetDataSourceNamePropertyName,
            128);
        dto.TargetProvider = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetProviderPropertyName,
            32);
        dto.TargetEndpoint = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetEndpointPropertyName,
            320);
        dto.TargetDatabase = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetDatabasePropertyName,
            128);
        dto.TargetTable = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetTablePropertyName,
            PipelineExecutionConsts.MaxCanonicalTargetTableLength);
        dto.TargetConfigVersion = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetConfigVersionPropertyName,
            40);
        dto.TargetResourceGroup = ReadString(
            execution,
            DataPipelineExecutionCoordinator.TargetResourceGroupPropertyName,
            128);
        var sourceId = ReadString(
            execution,
            DataPipelineExecutionCoordinator.SourceDataSourceIdPropertyName,
            32);
        dto.SourceDataSourceId = Guid.TryParseExact(
            sourceId,
            "N",
            out var parsedSourceId)
            ? parsedSourceId
            : null;
        dto.SourceDataSourceName = ReadString(
            execution,
            DataPipelineExecutionCoordinator.SourceDataSourceNamePropertyName,
            128);
        dto.SourceConfigVersion = ReadString(
            execution,
            DataPipelineExecutionCoordinator.SourceConfigVersionPropertyName,
            40);
        dto.WriteMode = ReadString(
            execution,
            DataPipelineExecutionCoordinator.WriteModePropertyName,
            16);
        dto.SourceQuerySha256 = ReadString(
            execution,
            DataPipelineExecutionCoordinator.SourceQuerySha256PropertyName,
            64);
        dto.ReconciliationStrategy = execution.EvidenceProtocol == 1
            ? "target-receipt-v1"
            : "manual-v0";
        dto.ReceiptVerificationEligible =
            DataPipelineExecutionCoordinator
                .IsTargetReceiptVerificationEligible(execution);
        dto.ReceiptVerificationSource = ReadString(
            execution,
            DataPipelineExecutionCoordinator
                .ReceiptVerificationSourcePropertyName,
            32);
        dto.ReceiptVerifiedAt = ReadTimestamp(
            execution,
            DataPipelineExecutionCoordinator.ReceiptVerifiedAtPropertyName);
    }

    private static string? ReadString(
        PipelineExecution execution,
        string key,
        int maxLength)
    {
        if (!execution.ExtraProperties.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value switch
        {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } json =>
                json.GetString(),
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(text) &&
               text.Length <= maxLength &&
               !text.Any(char.IsControl)
            ? text
            : null;
    }

    private static DateTimeOffset? ReadTimestamp(
        PipelineExecution execution,
        string key)
    {
        if (!execution.ExtraProperties.TryGetValue(key, out var value))
        {
            return null;
        }

        switch (value)
        {
            case DateTimeOffset offset:
                return offset.ToUniversalTime();
            case DateTime dateTime:
                var utc = dateTime.Kind switch
                {
                    DateTimeKind.Utc => dateTime,
                    DateTimeKind.Local => dateTime.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
                };
                return new DateTimeOffset(utc);
            case JsonElement { ValueKind: JsonValueKind.String } json:
                value = json.GetString();
                break;
        }

        return value is string text &&
               DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
