using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.EnterpriseManagement;

/// <summary>企业信息</summary>
public class EnterpriseDto : FullAuditedEntityDto<Guid>
{
    /// <summary>企业编码</summary>
    public string Code { get; set; } = null!;
    /// <summary>企业名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>企业简称</summary>
    public string? ShortName { get; set; }
    /// <summary>联系人</summary>
    public string? ContactPerson { get; set; }
    /// <summary>联系电话</summary>
    public string? ContactPhone { get; set; }
    /// <summary>邮箱</summary>
    public string? Email { get; set; }
    /// <summary>地址</summary>
    public string? Address { get; set; }
    /// <summary>状态（0=正常/1=暂停/2=归档）</summary>
    public EnterpriseStatus Status { get; set; }
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
    /// <summary>是否主体企业（每租户唯一）</summary>
    public bool IsMain { get; set; }
}
