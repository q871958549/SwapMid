namespace SwapMid.BlobStoring;

/// <summary>
/// 头像相关常量（扩展属性名 / 大小上限的单一事实源，防多处硬编码漂移）。
/// 放 Domain.Shared（无外部依赖）：ModuleExtensionConfigurator 与上层共用；BlobStoring 容器类另在 Application 层
/// （需 Volo.Abp.BlobStoring 核心，Domain.Shared 不引核心）。
/// </summary>
public static class AvatarConsts
{
    /// <summary>IdentityUser 扩展属性名：存头像 blob 名（形如 "{userId:N}{ext}"）。空/无 = 未设头像。</summary>
    public const string BlobNamePropertyName = "AvatarBlobName";

    /// <summary>头像大小上限（2MB）：控制器 [RequestSizeLimit] 与应用层 UploadStreamGuard 共用同一口径。</summary>
    public const long MaxBytes = 2 * 1024 * 1024;
}
