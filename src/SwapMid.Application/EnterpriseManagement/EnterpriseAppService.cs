using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.EnterpriseManagement;

[Authorize(SwapMidPermissions.EnterpriseManagement.Enterprises.Default)]
public class EnterpriseAppService :
    CrudAppService<Enterprise, EnterpriseDto, Guid, GetEnterpriseListInput, EnterpriseCreateUpdateDto>,
    IEnterpriseAppService
{
    private readonly IEnterpriseRepository _enterpriseRepository;

    public EnterpriseAppService(
        IRepository<Enterprise, Guid> repository,
        IEnterpriseRepository enterpriseRepository)
        : base(repository)
    {
        _enterpriseRepository = enterpriseRepository;
        GetPolicyName = SwapMidPermissions.EnterpriseManagement.Enterprises.Default;
        GetListPolicyName = SwapMidPermissions.EnterpriseManagement.Enterprises.Default;
        CreatePolicyName = SwapMidPermissions.EnterpriseManagement.Enterprises.Create;
        UpdatePolicyName = SwapMidPermissions.EnterpriseManagement.Enterprises.Update;
        DeletePolicyName = SwapMidPermissions.EnterpriseManagement.Enterprises.Delete;
    }

    public override async Task<PagedResultDto<EnterpriseDto>> GetListAsync(GetEnterpriseListInput input)
    {
        var query = await Repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            query = query.Where(x =>
                x.Code.Contains(input.Filter) ||
                x.Name.Contains(input.Filter) ||
                (x.ShortName != null && x.ShortName.Contains(input.Filter)));
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(x => x.Code)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<EnterpriseDto>(totalCount, items.Select(x => x.ToDto()).ToList());
    }

    public override async Task<EnterpriseDto> GetAsync(Guid id)
    {
        var enterprise = await Repository.GetAsync(id);
        return enterprise.ToDto();
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Enterprises.Create)]
    public override async Task<EnterpriseDto> CreateAsync(EnterpriseCreateUpdateDto input)
    {
        if (await _enterpriseRepository.CodeExistsAsync(input.Code))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseCodeAlreadyExists)
                .WithData("code", input.Code);
        }

        var enterprise = new Enterprise(
            GuidGenerator.Create(),
            input.Code,
            input.Name,
            input.ShortName,
            input.ContactPerson,
            input.ContactPhone,
            input.Email,
            input.Address);
        ApplyState(enterprise, input);

        await Repository.InsertAsync(enterprise, autoSave: true);
        return enterprise.ToDto();
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Enterprises.Update)]
    public override async Task<EnterpriseDto> UpdateAsync(Guid id, EnterpriseCreateUpdateDto input)
    {
        var enterprise = await Repository.GetAsync(id);

        if (await _enterpriseRepository.CodeExistsAsync(input.Code, excludeId: id))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.EnterpriseManagement.EnterpriseCodeAlreadyExists)
                .WithData("code", input.Code);
        }

        enterprise.Update(
            input.Code,
            input.Name,
            input.ShortName,
            input.ContactPerson,
            input.ContactPhone,
            input.Email,
            input.Address);
        ApplyState(enterprise, input);

        await Repository.UpdateAsync(enterprise, autoSave: true);
        return enterprise.ToDto();
    }

    [Authorize(SwapMidPermissions.EnterpriseManagement.Enterprises.Delete)]
    public override Task DeleteAsync(Guid id) => base.DeleteAsync(id);

    [Authorize(SwapMidPermissions.EnterpriseManagement.Enterprises.Update)]
    public async Task SetAsMainAsync(Guid id)
    {
        var query = await Repository.GetQueryableAsync();
        var currentMain = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.IsMain));
        if (currentMain != null && currentMain.Id != id)
        {
            currentMain.UnsetMain();
            await Repository.UpdateAsync(currentMain);
        }

        var enterprise = await Repository.GetAsync(id);
        enterprise.SetAsMain();
        await Repository.UpdateAsync(enterprise, autoSave: true);
    }

    public async Task<EnterpriseDto?> GetMainAsync()
    {
        var query = await Repository.GetQueryableAsync();
        var main = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.IsMain));
        return main?.ToDto();
    }

    private static void ApplyState(Enterprise enterprise, EnterpriseCreateUpdateDto input)
    {
        if (input.Status == EnterpriseStatus.Active)
        {
            enterprise.Activate();
            if (!input.IsEnabled)
            {
                enterprise.Disable();
            }

            return;
        }

        if (input.Status == EnterpriseStatus.Suspended)
        {
            enterprise.Suspend();
            return;
        }

        enterprise.Archive();
    }
}
