using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SwapMid.Settings;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace SwapMid.Layout;

/// <summary>
/// 表格视图（列序/显隐/列宽/固定列/每页条数/预设）与行密度云端个人偏好服务（User 级存储于 AbpSettings）。
/// </summary>
[Authorize]
public class TablePreferenceAppService : ApplicationService, ITablePreferenceAppService
{
    internal const int MaxSerializedViewsLength = 128 * 1024;
    private const int MaxTableEntries = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;

    public TablePreferenceAppService(ISettingProvider settingProvider, ISettingManager settingManager)
    {
        _settingProvider = settingProvider;
        _settingManager = settingManager;
    }

    public async Task<TablePreferenceSnapshotDto> GetAsync()
    {
        var rawDensity = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceTableDensity);
        var rawViews = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceTableViews);
        return new TablePreferenceSnapshotDto
        {
            Density = NormalizeDensity(rawDensity),
            Tables = ParseTables(rawViews),
        };
    }

    public async Task SaveTableViewAsync(SaveTableViewPreferenceInput input)
    {
        Check.NotNull(input, nameof(input));
        var tableKey = input.TableKey?.Trim();
        if (string.IsNullOrWhiteSpace(tableKey) || tableKey.Length > 128)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.Layout.TablePreferenceTooLong);
        }

        var rawViews = await _settingProvider.GetOrNullAsync(SwapMidSettings.PreferenceTableViews);
        var tables = ParseTables(rawViews);
        if (input.View is null)
        {
            tables.Remove(tableKey);
        }
        else
        {
            if (!tables.ContainsKey(tableKey) && tables.Count >= MaxTableEntries)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.Layout.TablePreferenceTooLong);
            }

            tables[tableKey] = input.View;
        }

        var serialized = JsonSerializer.Serialize(tables, JsonOptions);
        if (serialized.Length > MaxSerializedViewsLength)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.Layout.TablePreferenceTooLong);
        }

        await _settingManager.SetForCurrentUserAsync(SwapMidSettings.PreferenceTableViews, serialized);
    }

    public async Task SaveDensityAsync(SaveTableDensityInput input)
    {
        Check.NotNull(input, nameof(input));
        var density = NormalizeDensity(input.Density);
        await _settingManager.SetForCurrentUserAsync(SwapMidSettings.PreferenceTableDensity, density);
    }

    private static string NormalizeDensity(string? raw)
        => string.Equals(raw?.Trim(), "compact", StringComparison.OrdinalIgnoreCase)
            ? "compact"
            : "standard";

    private static Dictionary<string, TableViewPreferenceDto> ParseTables(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new Dictionary<string, TableViewPreferenceDto>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, TableViewPreferenceDto>>(rawJson, JsonOptions);
            return parsed != null
                ? new Dictionary<string, TableViewPreferenceDto>(parsed, StringComparer.Ordinal)
                : new Dictionary<string, TableViewPreferenceDto>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, TableViewPreferenceDto>(StringComparer.Ordinal);
        }
    }
}
