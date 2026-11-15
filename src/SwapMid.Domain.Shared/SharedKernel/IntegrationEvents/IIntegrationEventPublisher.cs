using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.IntegrationEvents;

/// <summary>
/// 平台内置跨引擎集成事件主题常量。
/// </summary>
public static class IntegrationTopicNames
{
    public const string PipelineEvents = "integration.pipeline.events";
    public const string CdcEvents = "integration.cdc.events";
}

/// <summary>
/// 跨引擎集成事件信封（位于 SharedKernel，保持业务域零交叉引用）。
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string TopicName,
    string Subject,
    string PayloadJson);

/// <summary>
/// 跨引擎集成事件发布端口：由 MessageBus 实现，供其他集成引擎在后台异步幂等投递。
/// </summary>
public interface IIntegrationEventPublisher
{
    Task<bool> PublishIfTopicActiveAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 SHA-256 构造确定性、符合 RFC 4122 版本位与变体位的事件幂等 Guid。
/// </summary>
public static class DeterministicEventId
{
    public static Guid Create(string canonicalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalKey);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey), hash);

        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);

        // RFC 4122 version 5 (name-based) & variant 10xx
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
