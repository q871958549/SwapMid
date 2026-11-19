using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace SwapMid.DataIntegration;

public static class CdcWebhookDeliveryConsts
{
    public const int MaxDeliveryIdLength = 128;
    public const int MaxCheckpointLsnLength = 128;
    public const int MaxErrorCodeLength = 64;
    public const int MaxErrorSummaryLength = 512;
    public const int MaxStoredPayloadBytes = 64 * 1024;
    public const int MaxStoredPayloadLength = 65536;
}

/// <summary>
/// Push Webhook CDC 认证推送投递与死信重放台账实体：
/// 记录每次 Webhook 推送结果；仅对已通过 HMAC-SHA256 验签但因流未激活或下游故障未能投递的批次保留有界（≤ 64 KiB）载荷以支持人工一键重放，
/// 未通过验签的伪造请求绝不落盘原始载荷以防止存储放大攻击。
/// </summary>
public class CdcWebhookDeliveryRecord : Entity<Guid>
{
    public Guid StreamId { get; private set; }
    public string DeliveryId { get; private set; } = string.Empty;
    public int Outcome { get; private set; }
    public int HttpStatusCode { get; private set; }
    public long AcceptedEventCount { get; private set; }
    public long DeliveredCount { get; private set; }
    public string? CheckpointLsn { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorSummary { get; private set; }
    public int PayloadSizeBytes { get; private set; }
    public string? StoredPayloadJson { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ReplayedAtUtc { get; private set; }
    public int ReplayCount { get; private set; }

    public bool CanReplay => !string.IsNullOrEmpty(StoredPayloadJson) && Outcome is (3 or 7);

    private CdcWebhookDeliveryRecord() { }

    public CdcWebhookDeliveryRecord(
        Guid id,
        Guid streamId,
        string deliveryId,
        int outcome,
        int httpStatusCode,
        long acceptedEventCount,
        long deliveredCount,
        string? checkpointLsn,
        string? errorCode,
        string? errorSummary,
        int payloadSizeBytes,
        string? storedPayloadJson,
        DateTime receivedAtUtc)
        : base(id)
    {
        StreamId = streamId;
        DeliveryId = Truncate(Check.NotNullOrWhiteSpace(deliveryId, nameof(deliveryId)), CdcWebhookDeliveryConsts.MaxDeliveryIdLength);
        Outcome = outcome;
        HttpStatusCode = httpStatusCode;
        AcceptedEventCount = Math.Max(0, acceptedEventCount);
        DeliveredCount = Math.Max(0, deliveredCount);
        CheckpointLsn = TruncateNullable(checkpointLsn, CdcWebhookDeliveryConsts.MaxCheckpointLsnLength);
        ErrorCode = TruncateNullable(errorCode, CdcWebhookDeliveryConsts.MaxErrorCodeLength);
        ErrorSummary = TruncateNullable(errorSummary, CdcWebhookDeliveryConsts.MaxErrorSummaryLength);
        PayloadSizeBytes = Math.Max(0, payloadSizeBytes);
        StoredPayloadJson = storedPayloadJson is { Length: > 0 and <= CdcWebhookDeliveryConsts.MaxStoredPayloadLength }
            ? storedPayloadJson
            : null;
        ReceivedAtUtc = receivedAtUtc;
    }

    public void MarkReplaySucceeded(
        long acceptedEventCount,
        long deliveredCount,
        string? checkpointLsn,
        DateTime replayedAtUtc)
    {
        Outcome = 0; // Accepted
        HttpStatusCode = 200;
        AcceptedEventCount = Math.Max(0, acceptedEventCount);
        DeliveredCount = Math.Max(0, deliveredCount);
        CheckpointLsn = TruncateNullable(checkpointLsn, CdcWebhookDeliveryConsts.MaxCheckpointLsnLength);
        ErrorCode = null;
        ErrorSummary = null;
        StoredPayloadJson = null;
        ReplayedAtUtc = replayedAtUtc;
        ReplayCount += 1;
    }

    public void MarkReplayFailed(string errorCode, string errorSummary, DateTime attemptedAtUtc)
    {
        Outcome = 7; // Failed
        HttpStatusCode = 500;
        ErrorCode = TruncateNullable(errorCode, CdcWebhookDeliveryConsts.MaxErrorCodeLength);
        ErrorSummary = TruncateNullable(errorSummary, CdcWebhookDeliveryConsts.MaxErrorSummaryLength);
        ReplayedAtUtc = attemptedAtUtc;
        ReplayCount += 1;
    }

    private static string Truncate(string value, int maxLen)
        => value.Length <= maxLen ? value : value[..maxLen];

    private static string? TruncateNullable(string? value, int maxLen)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLen);
}
