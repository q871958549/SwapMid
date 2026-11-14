using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.Uow;

namespace SwapMid.SystemManagement;

/// <summary>
/// 编码规则生成内核（§17 缺陷修复 + R1 并发加固，蓝图 docs/project-management-blueprint.md §20.3）：
/// 承载 Application 层 <c>CodeRuleAppService.GenerateAsync</c> 的生成逻辑，供无用户身份场景下的内部调用方使用。
///
/// 为什么存在：Hangfire 后台线程无 <c>CurrentUser</c>（匿名），而 <c>ICodeRuleAppService</c> 是接口注入——
/// ABP 接口代理拦截该接口的一切方法（不止 virtual 方法），凡带 <c>[Authorize]</c> 的调用在无身份线程必炸
/// <see cref="Volo.Abp.Authorization.AbpAuthorizationException"/>。本类是普通 <see cref="DomainService"/>
/// （非 AppService、无鉴权特性），后台任务（如样品数据自动同步造样品编号）改注入本类即可绕开该问题，
/// 且不提权、不新增匿名 HTTP 端点。HTTP 门面 <c>CodeRuleAppService.GenerateAsync</c> 保留
/// <c>[Authorize]</c> 一行委托本类，鉴权面与契约均零变化。
///
/// R1 并发加固：取号不再是「查行 → 内存改 → UpdateAsync」的无锁读改写（旧实现在全量同步等高并发场景下，
/// 因 <see cref="CodeRule"/> 带乐观并发戳，会让并发取号者互相抛 <see cref="Volo.Abp.Domain.Entities.Events.EntityChangedEventData{TEntity}"/>
/// 之外的 <c>AbpDbConcurrencyException</c>，且长事务调用方（手动导入整请求、Runner 外层大事务）会把计数器行锁
/// 持有到自己事务结束）。改为：独立 <c>requiresNew</c> 短事务 + 单条参数化原子 SQL <c>UPDATE ... RETURNING</c>
/// （<see cref="ICodeRuleRepository.AdvanceSequenceAsync"/>，EFCore 层实现，兼容 PostgreSQL/SQLite）——
/// 取号立即提交，调用方长事务不再拖住行锁；并发者在数据库行锁上排队，零业务异常。
/// </summary>
public class CodeRuleManager : DomainService
{
    private readonly ICodeRuleRepository _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public CodeRuleManager(ICodeRuleRepository repository, IUnitOfWorkManager unitOfWorkManager)
    {
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// 按实体类型生成下一个编码：独立短事务内原子推进序号（含按重置周期归零）→ 拼装编码。
    /// 规则不存在或未启用抛 <c>CodeRuleNotFound</c> 不变。时钟口径：R3 统一改用 <see cref="Volo.Abp.Timing.IClock.Now"/>
    /// （经 <see cref="DomainService.Clock"/> 取得，本地时钟口径）——消除凌晨 cron 生成的编码日期段与
    /// <c>RegisteredAt</c> 不一致的既有瑕疵。
    /// </summary>
    public async Task<string> GenerateAsync(string entityType)
    {
        var now = Clock.Now;

        CodeRuleSequenceRow? row;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            row = await _repository.AdvanceSequenceAsync(entityType, now);
            await uow.CompleteAsync();
        }

        if (row == null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.SystemManagement.CodeRuleNotFound)
                .WithData("entityType", entityType);
        }

        return CodeRule.BuildCode(row.Prefix, row.Suffix, row.IncludeDate, row.DateFormat, row.SequencePad, row.CurrentSequence, now);
    }
}
