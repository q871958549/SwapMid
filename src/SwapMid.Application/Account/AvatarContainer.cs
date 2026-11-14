using Volo.Abp.BlobStoring;

namespace SwapMid.Account;

/// <summary>
/// 用户头像 blob 容器。BlobStoring database provider（模块默认后端）→ 落 AbpBlobs 表，随 pg_dump 备份、
/// 免挂 Docker 卷。头像小、每用户一张、覆盖式保存。放 Application 层（可访问 Volo.Abp.BlobStoring 核心）。
/// </summary>
[BlobContainerName("avatars")]
public class AvatarContainer
{
}
