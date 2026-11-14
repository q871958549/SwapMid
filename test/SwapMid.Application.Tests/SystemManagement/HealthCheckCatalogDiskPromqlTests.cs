using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.SystemManagement;

public class HealthCheckCatalogDiskPromqlTests
{
    private const string LocalFsExclude = "tmpfs|overlay|squashfs|cifs|nfs|nfs4|fuse.*";

    [Fact]
    public void 主机磁盘使用率必须排除网络挂载且对齐主机告警阈值()
    {
        var disk = HealthCheckCatalog.Items.Single(item => item.Name == "主机磁盘使用率");

        disk.PromQl.ShouldBe(
            "max((1 - node_filesystem_avail_bytes{fstype!~\"" + LocalFsExclude
            + "\"} / node_filesystem_size_bytes{fstype!~\"" + LocalFsExclude + "\"}) * 100)");
        disk.WarnThreshold.ShouldBe(85);
        disk.CriticalThreshold.ShouldBe(92);
    }

    [Fact]
    public void 数据管道执行失败率检查项必须对齐近一小时窗口与阈值()
    {
        var etl = HealthCheckCatalog.Items.Single(item => item.Name == "数据管道执行失败率");

        etl.PromQl.ShouldBe(
            "sum(increase(swapmid_etl_execution_results_total{outcome!=\"succeeded\"}[1h])) / sum(increase(swapmid_etl_execution_results_total[1h]))");
        etl.WarnThreshold.ShouldBe(0.1);
        etl.CriticalThreshold.ShouldBe(0.3);
    }
}
