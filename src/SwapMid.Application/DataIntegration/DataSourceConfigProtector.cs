using System;

namespace SwapMid.DataIntegration;

/// <summary>
/// 数据源配置的单一安全边界。
/// 读侧采用按类型的字段白名单；写侧只允许关系型配置在同一凭据作用域内沿用旧密码。
/// 无稳定配置契约的 REST/File 不做猜测式脱敏或秘密合并。
/// </summary>
internal static class DataSourceConfigProtector
{
    internal const string SafeEmptyConfig = "{}";

    /// <summary>所有 DataSource 读响应必须经过此入口，禁止直接映射实体 Config。</summary>
    internal static DataSourceDto ToResponseDto(DataSource dataSource)
    {
        var dto = dataSource.ToDto();
        var protectedConfig = SanitizeForRead(dataSource.Type, dataSource.Config);
        dto.Config = protectedConfig.Config;
        dto.HasSecret = protectedConfig.HasSecret;
        return dto;
    }

    /// <summary>
    /// 构造只读配置：关系型仅输出强类型白名单字段；REST/File 固定输出空对象。
    /// 非法 JSON、未知非空字段和不可验证值均保守标记 <c>HasSecret=true</c>。
    /// </summary>
    internal static DataSourceProtectedConfig SanitizeForRead(
        DataSourceType type,
        string? config)
    {
        if (!DataSourceConfigJson.TryParseObject(config, out var root))
        {
            return new DataSourceProtectedConfig(SafeEmptyConfig, HasSecret: true);
        }

        if (type != DataSourceType.RelationalDatabase)
        {
            return new DataSourceProtectedConfig(
                SafeEmptyConfig,
                HasSecret: DataSourceConfigJson.HasMeaningfulContent(root));
        }

        return RelationalDataSourceConfigContract.SanitizeForRead(root);
    }

    /// <summary>校验并规范待创建配置；关系型只保存强类型字段和可选密码。</summary>
    internal static string NormalizeForWrite(DataSourceType type, string? config)
    {
        if (!Enum.IsDefined(type) ||
            !DataSourceConfigJson.TryParseObject(config, out var root))
        {
            throw new DataSourceConfigFormatException();
        }

        var normalized = type == DataSourceType.RelationalDatabase
            ? RelationalDataSourceConfigContract.ParseForWrite(root).ToJson(includePassword: true)
            : root.ToJsonString();
        return EnsureStorageLength(normalized);
    }

    /// <summary>
    /// 合并更新配置。关系型仅在 provider/host/port/database/username/TLS 姿态全部
    /// 规范化相等时恢复旧 password；REST/File 的旧非空配置必须由完整非空新对象整体替换。
    /// targetResourceGroup 是非秘密的执行隔离标识，不属于数据库凭据作用域。
    /// </summary>
    internal static string MergeForWrite(
        DataSourceType storedType,
        string? storedConfig,
        DataSourceType proposedType,
        string? proposedConfig)
    {
        if (!Enum.IsDefined(proposedType) ||
            !DataSourceConfigJson.TryParseObject(proposedConfig, out var proposed))
        {
            throw new DataSourceConfigFormatException();
        }

        if (proposedType != DataSourceType.RelationalDatabase)
        {
            return MergeOpaqueForWrite(storedConfig, proposed);
        }

        var proposedRelational = RelationalDataSourceConfigContract.ParseForWrite(proposed);
        if (storedType != DataSourceType.RelationalDatabase)
        {
            // 类型变化是完整替换，旧类型（即使原文已畸形）的任何秘密都不参与。
            return EnsureStorageLength(proposedRelational.ToJson(includePassword: true));
        }

        if (!DataSourceConfigJson.TryParseObject(storedConfig, out var stored))
        {
            // 旧 JSON 已无法解析，不能恢复其中任何凭据；显式新密码构成完整、
            // 可验证的关系型替换，使管理员无需删记录即可自愈。
            return RequireExplicitRelationalReplacement(proposedRelational);
        }

        RelationalDataSourceWriteConfig storedRelational;
        try
        {
            storedRelational = RelationalDataSourceConfigContract.ParseForWrite(stored);
        }
        catch (DataSourceConfigFormatException)
        {
            // 旧关系型配置不满足强类型契约时无法证明目标相同，禁止猜测式恢复。
            return RequireExplicitRelationalReplacement(proposedRelational);
        }

        var sameScope = storedRelational.Scope == proposedRelational.Scope;
        if (!sameScope && string.IsNullOrWhiteSpace(proposedRelational.Password))
        {
            throw new DataSourceCredentialScopeChangedException();
        }

        if (sameScope &&
            !string.IsNullOrWhiteSpace(storedRelational.Password) &&
            string.IsNullOrWhiteSpace(proposedRelational.Password))
        {
            proposedRelational = proposedRelational with { Password = storedRelational.Password };
        }

        return EnsureStorageLength(proposedRelational.ToJson(includePassword: true));
    }

    private static string MergeOpaqueForWrite(
        string? storedConfig,
        System.Text.Json.Nodes.JsonObject proposed)
    {
        if (!DataSourceConfigJson.TryParseObject(storedConfig, out var storedOpaque))
        {
            // 旧值已畸形时，仍允许管理员用完整非空对象修复；脱敏空对象不能
            // 被误判为“完整替换”，否则会静默擦除无法读取的存量秘密。
            if (!DataSourceConfigJson.HasMeaningfulContent(proposed))
            {
                throw new DataSourceConfigReplacementRequiredException();
            }

            return EnsureStorageLength(proposed.ToJsonString());
        }

        if (DataSourceConfigJson.HasMeaningfulContent(storedOpaque) &&
            !DataSourceConfigJson.HasMeaningfulContent(proposed))
        {
            throw new DataSourceConfigReplacementRequiredException();
        }

        // REST/File 没有稳定 schema，调用方提供的非空 JSON 被视为完整替换；
        // 绝不从旧对象复制任何 token/secret/authorization。
        return EnsureStorageLength(proposed.ToJsonString());
    }

    private static string RequireExplicitRelationalReplacement(
        RelationalDataSourceWriteConfig proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed.Password))
        {
            throw new DataSourceCredentialScopeChangedException();
        }

        return EnsureStorageLength(proposed.ToJson(includePassword: true));
    }

    private static string EnsureStorageLength(string normalized)
    {
        if (normalized.Length > DataSourceConsts.MaxConfigLength)
        {
            throw new DataSourceConfigFormatException();
        }

        return normalized;
    }
}

internal readonly record struct DataSourceProtectedConfig(string Config, bool HasSecret);

internal sealed class DataSourceConfigFormatException : Exception;

internal sealed class DataSourceCredentialScopeChangedException : Exception;

internal sealed class DataSourceConfigReplacementRequiredException : Exception;
