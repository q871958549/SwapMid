using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using SwapMid.BlobStoring;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace SwapMid.Account;

/// <summary>
/// 用户头像存取实现：字节走 <see cref="IBlobContainer{AvatarContainer}"/>（database provider → AbpBlobs 表），
/// 引用挂 IdentityUser 的 AvatarBlobName 扩展属性。上传经 <see cref="UploadStreamGuard"/> 限长读入内存。
/// </summary>
public class AvatarManager : IAvatarManager, ITransientDependency
{
    private readonly IBlobContainer<AvatarContainer> _blobContainer;
    private readonly IdentityUserManager _userManager;

    public AvatarManager(IBlobContainer<AvatarContainer> blobContainer, IdentityUserManager userManager)
    {
        _blobContainer = blobContainer;
        _userManager = userManager;
    }

    public async Task SaveAsync(Guid userId, Stream content, string extension)
    {
        // 限长读入内存（超额立即中断，不整份进内存）；控制器 [RequestSizeLimit] 已在 Kestrel 层兜底，两层同口径。
        await using var bounded = await UploadStreamGuard.CopyToMemoryBoundedAsync(
            content, AvatarConsts.MaxBytes, "头像图片过大（上限 2MB）");
        var bytes = bounded.ToArray();

        var user = await _userManager.FindByIdAsync(userId.ToString());   // 当前用户，必存在
        if (user == null)
        {
            throw new UserFriendlyException("用户不存在");
        }
        var blobName = $"{userId:N}{extension}";

        // 换格式时旧 blob 名不同，先删旧再存新，防残留（同名则 overrideExisting 覆盖）。
        var oldName = user.GetProperty<string>(AvatarConsts.BlobNamePropertyName);
        if (!string.IsNullOrEmpty(oldName) && oldName != blobName)
        {
            await _blobContainer.DeleteAsync(oldName);
        }

        await _blobContainer.SaveAsync(blobName, bytes, overrideExisting: true);

        user.SetProperty(AvatarConsts.BlobNamePropertyName, blobName);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    public async Task<AvatarContent?> GetAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        var blobName = user?.GetProperty<string>(AvatarConsts.BlobNamePropertyName);
        if (string.IsNullOrEmpty(blobName))
        {
            return null;
        }

        var bytes = await _blobContainer.GetAllBytesOrNullAsync(blobName);
        return bytes == null ? null : new AvatarContent(bytes, ResolveContentType(blobName));
    }

    private static string ResolveContentType(string blobName)
        => Path.GetExtension(blobName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
}
