using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// Push Webhook CDC 认证推送接入结果码。
/// </summary>
public enum CdcWebhookIngestOutcome
{
    Accepted = 0,
    NotFound = 1,
    Unauthorized = 2,
    Inactive = 3,
    InvalidPayload = 4,
    PayloadTooLarge = 5,
    Busy = 6,
    Failed = 7,
}

/// <summary>
/// Push Webhook CDC 认证推送接入响应 DTO。
/// </summary>
public class CdcWebhookIngestResultDto
{
    public Guid StreamId { get; set; }
    public string DeliveryId { get; set; } = string.Empty;
    public long AcceptedEventCount { get; set; }
    public long DeliveredCount { get; set; }
    public string? CheckpointLsn { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Push Webhook CDC 单次推送/死信台账记录 DTO。
/// </summary>
public class CdcWebhookDeliveryRecordDto
{
    public Guid Id { get; set; }
    public Guid StreamId { get; set; }
    public string DeliveryId { get; set; } = string.Empty;
    public CdcWebhookIngestOutcome Outcome { get; set; }
    public int HttpStatusCode { get; set; }
    public long AcceptedEventCount { get; set; }
    public long DeliveredCount { get; set; }
    public string? CheckpointLsn { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorSummary { get; set; }
    public int PayloadSizeBytes { get; set; }
    public bool CanReplay { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ReplayedAtUtc { get; set; }
    public int ReplayCount { get; set; }
}

/// <summary>
/// Push Webhook CDC 推送台账列表响应 DTO。
/// </summary>
public class CdcWebhookDeliveryListDto
{
    public Guid StreamId { get; set; }
    public int TotalCount { get; set; }
    public int ReplayableCount { get; set; }
    public List<CdcWebhookDeliveryRecordDto> Items { get; set; } = new();
}

/// <summary>
/// Push Webhook CDC 内部执行结果（包含结构化状态码与对外安全响应）。
/// </summary>
public sealed record CdcWebhookIngestExecutionResult(
    CdcWebhookIngestOutcome Outcome,
    string ErrorCode,
    string ErrorMessage,
    CdcWebhookIngestResultDto? Result = null);

/// <summary>
/// Serverless Push Webhook CDC 认证事件推送与死信重放服务契约（非 ABP IApplicationService，由独立 Webhook 控制器调用）。
/// </summary>
public interface ICdcWebhookIngressService
{
    Task<CdcWebhookIngestExecutionResult> IngestAsync(
        Guid streamId,
        byte[] rawBodyBytes,
        string? signatureHeader,
        string? deliveryIdHeader,
        CancellationToken cancellationToken = default);

    Task<CdcWebhookDeliveryListDto> GetDeliveriesAsync(
        Guid streamId,
        int maxCount = 30,
        CancellationToken cancellationToken = default);

    Task<CdcWebhookIngestResultDto> ReplayDeliveryAsync(
        Guid streamId,
        Guid deliveryRecordId,
        CancellationToken cancellationToken = default);

    void RecordPayloadTooLarge();
}
