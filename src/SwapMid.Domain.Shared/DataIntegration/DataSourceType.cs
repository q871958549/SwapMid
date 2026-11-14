using System.ComponentModel;

namespace SwapMid.DataIntegration;

public enum DataSourceType
{
    [Description("关系型数据库")]
    RelationalDatabase = 0,

    [Description("REST API")]
    RestApi = 1,

    [Description("文件")]
    File = 2
}
