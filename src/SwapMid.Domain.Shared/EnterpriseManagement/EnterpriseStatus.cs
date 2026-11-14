using System.ComponentModel;

namespace SwapMid.EnterpriseManagement;

public enum EnterpriseStatus
{
    [Description("正常")]
    Active = 0,

    [Description("暂停")]
    Suspended = 1,

    [Description("归档")]
    Archived = 2
}
