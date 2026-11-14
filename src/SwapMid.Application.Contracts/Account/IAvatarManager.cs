using System;
using System.IO;
using System.Threading.Tasks;

namespace SwapMid.Account;

/// <summary>
/// 用户头像存取。**刻意不继承 IApplicationService** —— 端点形态（IFormFile 上传 / FileContentResult 下载）
/// 由 AvatarController 手控，不走 ABP auto API。字节走 BlobStoring，引用挂 IdentityUser 扩展属性 AvatarBlobName。
/// 走 IdentityUserManager 改用户（无 [Authorize]，与 EmployeeAccountProvisioner 同款）。
/// </summary>
public interface IAvatarManager
{
    /// <summary>保存指定用户头像（限长读入内存后存 blob；换格式时删旧 blob；写扩展属性）。extension 含点，如 ".png"。</summary>
    Task SaveAsync(Guid userId, Stream content, string extension);

    /// <summary>读指定用户头像字节 + MIME；无头像返回 null。</summary>
    Task<AvatarContent?> GetAsync(Guid userId);
}

/// <summary>头像字节 + MIME 类型。</summary>
public record AvatarContent(byte[] Content, string ContentType);
