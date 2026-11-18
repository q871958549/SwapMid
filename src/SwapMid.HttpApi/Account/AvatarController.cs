using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Account;
using SwapMid.BlobStoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Users;

namespace SwapMid.Controllers.Account;

/// <summary>
/// 用户头像上传/下载。上传 = 当前用户给自己传（仅 [Authorize]、不挂具体权限，全员可管理自己头像）；
/// 下载 = 按 userId 取（登录可见，头像非敏感）。字节走 BlobStoring，走**鉴权通道**（非 Out 匿名静态）。
/// </summary>
[RemoteService]
[Area("app")]
[Route("api/v1/avatars")]
[Authorize]
public class AvatarController : SwapMidController
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    private readonly IAvatarManager _avatarManager;
    private readonly ICurrentUser _currentUser;

    public AvatarController(IAvatarManager avatarManager, ICurrentUser currentUser)
    {
        _avatarManager = avatarManager;
        _currentUser = currentUser;
    }

    /// <summary>当前用户上传/替换头像（jpg/png/webp，≤2MB）。</summary>
    [HttpPost]
    [RequestSizeLimit(AvatarConsts.MaxBytes)]
    // 注意：IFormFile 参数不能加 [FromForm]——Swashbuckle 不支持该组合，会让整份 swagger.json 生成 500。
    public async Task UploadAsync(IFormFile file)
    {
        if (file == null || file.Length <= 0 || string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new UserFriendlyException("请选择要上传的头像图片");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new UserFriendlyException(
                $"不支持的图片类型「{extension}」，允许：{string.Join("、", AllowedExtensions.OrderBy(x => x))}");
        }

        await using var stream = file.OpenReadStream();
        await _avatarManager.SaveAsync(_currentUser.GetId(), stream, extension.ToLowerInvariant());
    }

    /// <summary>当前用户头像（header / 个人中心用，免在前端拼 userId）。</summary>
    [HttpGet("me")]
    public Task<IActionResult> GetMineAsync()
        => GetAsync(_currentUser.GetId());

    /// <summary>指定用户头像；无自定义头像返回 204 NoContent，前端 AvatarImage 自动回退首字母。</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetAsync(Guid userId)
    {
        var avatar = await _avatarManager.GetAsync(userId);
        if (avatar == null)
        {
            return NoContent();
        }

        // no-cache：头像 URL 固定（/avatars/me、/avatars/{id}），换头像后 URL 不变；令浏览器每次校验，
        // 避免 header / 用户列表显示旧缓存图（个人中心页另用 ?v= 时间戳即时破缓存）。
        Response.Headers.CacheControl = "no-cache";
        // nosniff：拒绝浏览器把响应体嗅探成非声明类型（纵深防御，配合扩展名白名单派生的 image/* ContentType）。
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(avatar.Content, avatar.ContentType);
    }
}
