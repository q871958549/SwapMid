using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.Uow;

namespace SwapMid.ApiManagement;

public sealed class GatewayClientGovernanceReader : IGatewayClientGovernanceReader, ITransientDependency
{
    public const string PolicyPropertyKey = "swapmid.gateway.policy";

    private readonly IOpenIddictApplicationRepository _applicationRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public GatewayClientGovernanceReader(
        IOpenIddictApplicationRepository applicationRepository,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _applicationRepository = applicationRepository;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<GatewayClientPolicySnapshot?> FindPolicyAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var normalized = clientId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var entity = await _applicationRepository.FindByClientIdAsync(normalized, cancellationToken);
        await uow.CompleteAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var (allowedGroups, rateLimit) = ReadFromPropertiesJson(entity.Properties);
        return new GatewayClientPolicySnapshot(
            entity.ClientId ?? normalized,
            allowedGroups.ToHashSet(),
            rateLimit);
    }

    internal static (List<Guid> AllowedGroupIds, int? RateLimitPerMinute) ReadFromPropertiesJson(
        string? propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            return ([], null);
        }

        try
        {
            using var doc = JsonDocument.Parse(propertiesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(PolicyPropertyKey, out var policyElement) ||
                policyElement.ValueKind != JsonValueKind.Object)
            {
                return ([], null);
            }

            var groups = new List<Guid>();
            if (policyElement.TryGetProperty("allowedGroupIds", out var groupsElement) &&
                groupsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in groupsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(item.GetString(), out var groupId) &&
                        groupId != Guid.Empty &&
                        !groups.Contains(groupId))
                    {
                        groups.Add(groupId);
                    }
                }
            }

            int? rateLimit = null;
            if (policyElement.TryGetProperty("rateLimitPerMinute", out var rateElement) &&
                rateElement.ValueKind == JsonValueKind.Number &&
                rateElement.TryGetInt32(out var parsedRate) &&
                parsedRate is >= 1 and <= 60_000)
            {
                rateLimit = parsedRate;
            }

            return (groups, rateLimit);
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }

    internal static JsonElement SerializePolicyElement(
        IEnumerable<Guid>? allowedGroupIds,
        int? rateLimitPerMinute)
    {
        var normalizedGroups = (allowedGroupIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var normalizedRate = rateLimitPerMinute is >= 1 and <= 60_000
            ? rateLimitPerMinute
            : null;
        return JsonSerializer.SerializeToElement(new
        {
            allowedGroupIds = normalizedGroups,
            rateLimitPerMinute = normalizedRate,
        });
    }
}
