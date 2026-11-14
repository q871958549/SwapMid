using Xunit;

namespace SwapMid.EntityFrameworkCore;

/// <summary>
/// 本程序集的受控并行分组名。
///
/// 背景(2026-07-29):此前 100 个构建 ABP Application 的测试类全部挂在单一
/// <c>SwapMidTestConsts.CollectionDefinitionName</c> 上。xunit v2 以「测试集合」为
/// 并行调度单位、集合内测试类串行,于是这 100 个类实际是**完全串行**执行的——CI 实测
/// 825 个测试耗时 16m56s(约 1.24s/测试,几乎就是纯累加),独占整条 CI 关键路径。
///
/// 现按业务域拆成下列若干集合:**集合之间并行、集合之内仍串行**。并行安全的前提是每个
/// [Fact] 已持有独立容器 + 独立 <c>:memory:</c> SQLite 连接(见 TestBase),测试之间不共享
/// 进程级可变状态——最后一处例外(FakeMessageDelivery* 的 static 集合)已随本次改造改为容器单例
/// <see cref="MessageBus.MessageDeliveryRecorder"/>。
///
/// ⚠️ 两个 <c>DisableParallelization = true</c> 的专属集合(招聘入职真事务 / 授权拦截器)不在此列,
/// 保持原样,勿并入下列任何分组。
///
/// 新增测试类时:按其所在业务域选一个集合挂上;域不在下列时归入 <see cref="Platform"/> 或
/// <see cref="Enterprise"/>。<c>TestParallelismConventionTests</c> 会强制"必须显式挂受控集合",
/// 游离(不写 [Collection])仍然是错误——那会退化成与所有集合并行的隐式集合。
/// </summary>
public static class EntityFrameworkCoreTestCollections
{
    /// <summary>共享数据库集合夹具。</summary>
    public const string Sample = "EFCore:ProjectManagement.Sample";

    /// <summary>项目与计划(项目主数据 / 计划版本 / 模板 / 台账)。</summary>
    public const string Project = "EFCore:ProjectManagement.Project";

    /// <summary>招聘渠道(渠道账号 / 拉取链 / 附件预备 / 调度)。</summary>
    public const string RecruitmentChannel = "EFCore:Recruitment.Channel";

    /// <summary>招聘主流程(候选人 / 简历推送 / 面试 / Offer / 看板)。</summary>
    public const string RecruitmentCore = "EFCore:Recruitment.Core";

    /// <summary>平台化定时任务(各 Job Handler)。</summary>
    public const string Scheduling = "EFCore:Scheduling";

    /// <summary>平台基建(消息总线 / 数据集成 / API 管理 / 知识中心 / 开发者文档 / 对外匿名 Out)。</summary>
    public const string Platform = "EFCore:Platform";

    /// <summary>企业与业务查询(账号权限 / 组织 / 系统管理 / IT 资产 / 排产 / 报表 / WMS)。</summary>
    public const string Enterprise = "EFCore:Enterprise";
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.Sample)]
public class SampleTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.Project)]
public class ProjectTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.RecruitmentChannel)]
public class RecruitmentChannelTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.RecruitmentCore)]
public class RecruitmentCoreTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.Scheduling)]
public class SchedulingTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.Platform)]
public class PlatformTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}

[CollectionDefinition(EntityFrameworkCoreTestCollections.Enterprise)]
public class EnterpriseTestCollection : SwapMidEntityFrameworkCoreCollectionFixtureBase
{
}
