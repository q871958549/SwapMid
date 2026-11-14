using System.ComponentModel.DataAnnotations;

namespace SwapMid.EnterpriseManagement;

/// <summary>新建/更新企业入参</summary>
public class EnterpriseCreateUpdateDto
{
    /// <summary>企业编码</summary>
    [Required]
    [MaxLength(EnterpriseConsts.MaxCodeLength)]
    public string Code { get; set; } = null!;

    /// <summary>企业名称</summary>
    [Required]
    [MaxLength(EnterpriseConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>企业简称</summary>
    [MaxLength(EnterpriseConsts.MaxShortNameLength)]
    public string? ShortName { get; set; }

    /// <summary>联系人</summary>
    [MaxLength(EnterpriseConsts.MaxContactPersonLength)]
    public string? ContactPerson { get; set; }

    /// <summary>联系电话</summary>
    [MaxLength(EnterpriseConsts.MaxContactPhoneLength)]
    public string? ContactPhone { get; set; }

    /// <summary>邮箱</summary>
    [MaxLength(EnterpriseConsts.MaxEmailLength)]
    public string? Email { get; set; }

    /// <summary>地址</summary>
    [MaxLength(EnterpriseConsts.MaxAddressLength)]
    public string? Address { get; set; }

    /// <summary>状态（0=正常/1=暂停/2=归档）</summary>
    public EnterpriseStatus Status { get; set; } = EnterpriseStatus.Active;
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
