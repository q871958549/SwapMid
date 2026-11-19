using System;
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
/// Push Webhook CDC 内部执行结果（包含结构化状态码与对外安全响应）。
/// </summary>
public sealed record CdcWebhookIngestExecutionResult(
    CdcWebhookIngestOutcome Outcome,
    string ErrorCode,
    string ErrorMessage,
    CdcWebhookIngestResultDto? Result = null);

/// <summary>
/// Serverless Push Webhook CDC 认证事件推送服务契约（非 ABP IApplicationService，由独立 Webhook 控制器调用）。
/// </summary>
public interface ICdcWebhookIngressService
{
    Task<CdcWebhookIngestExecutionResult> IngestAsync(
        Guid streamId,
        byte[] rawBodyBytes,
        string? signatureHeader,
        string? deliveryIdHeader,
        CancellationToken cancellationToken = default);

    void RecordPayloadTooLarge();
}
