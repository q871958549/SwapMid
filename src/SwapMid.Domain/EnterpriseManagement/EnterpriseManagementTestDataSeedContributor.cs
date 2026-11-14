using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace SwapMid.EnterpriseManagement;

public class EnterpriseManagementTestDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<Enterprise, Guid> _enterpriseRepository;
    private readonly IRepository<CompanyDepartment, Guid> _departmentRepository;
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IRepository<Position, Guid> _positionRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;

    public EnterpriseManagementTestDataSeedContributor(
        IRepository<Enterprise, Guid> enterpriseRepository,
        IRepository<CompanyDepartment, Guid> departmentRepository,
        IRepository<Employee, Guid> employeeRepository,
        IRepository<Position, Guid> positionRepository,
        IGuidGenerator guidGenerator,
        IAsyncQueryableExecuter asyncExecuter,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        _enterpriseRepository = enterpriseRepository;
        _departmentRepository = departmentRepository;
        _employeeRepository = employeeRepository;
        _positionRepository = positionRepository;
        _guidGenerator = guidGenerator;
        _asyncExecuter = asyncExecuter;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        var enabled = string.Equals(
            _configuration["EnterpriseManagement:EnableDemoData"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (!enabled)
        {
            return;
        }

        if (!_hostEnvironment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "EnterpriseManagement:EnableDemoData 仅允许在 Development 环境启用。");
        }

        var enterprises = await _asyncExecuter.ToListAsync(await _enterpriseRepository.GetQueryableAsync());
        var mainEnterprise = enterprises.FirstOrDefault(x => x.IsMain);
        if (mainEnterprise == null)
        {
            mainEnterprise = enterprises.FirstOrDefault(x => x.Code == "SWAPMID");
            if (mainEnterprise == null)
            {
                mainEnterprise = new Enterprise(
                    _guidGenerator.Create(),
                    "SWAPMID",
                    "SwapMid Open Integration",
                    shortName: "SwapMid",
                    contactPerson: "Platform Admin",
                    email: "admin@example.test");
                mainEnterprise.SetAsMain();
                await _enterpriseRepository.InsertAsync(mainEnterprise, autoSave: true);
            }
            else if (enterprises.All(x => !x.IsMain))
            {
                mainEnterprise.SetAsMain();
                await _enterpriseRepository.UpdateAsync(mainEnterprise, autoSave: true);
            }
        }

        var existingDepts = await _departmentRepository.GetListAsync(x => x.EnterpriseId == mainEnterprise.Id);
        if (existingDepts.Count > 0)
        {
            return;
        }

        var deptPlatform = new CompanyDepartment(_guidGenerator.Create(), mainEnterprise.Id, "DEPT-PLATFORM", "集成平台部", order: 1);
        deptPlatform.SetHierarchy(1, "集成平台部");

        var deptData = new CompanyDepartment(_guidGenerator.Create(), mainEnterprise.Id, "DEPT-DATA", "数据架构组", parentId: deptPlatform.Id, order: 1);
        deptData.SetHierarchy(2, "集成平台部/数据架构组");

        var deptMiddleware = new CompanyDepartment(_guidGenerator.Create(), mainEnterprise.Id, "DEPT-MW", "中间件与网关组", parentId: deptPlatform.Id, order: 2);
        deptMiddleware.SetHierarchy(2, "集成平台部/中间件与网关组");

        var deptOps = new CompanyDepartment(_guidGenerator.Create(), mainEnterprise.Id, "DEPT-OPS", "平台运维与安全部", order: 2);
        deptOps.SetHierarchy(1, "平台运维与安全部");

        var allDepts = new List<CompanyDepartment> { deptPlatform, deptData, deptMiddleware, deptOps };
        await _departmentRepository.InsertManyAsync(allDepts, autoSave: true);

        var posLead = new Position(_guidGenerator.Create(), mainEnterprise.Id, deptPlatform.Id, "POS-ARCH", "集成架构师", level: "P7", description: "负责企业集成架构设计", order: 1);
        var posDataEng = new Position(_guidGenerator.Create(), mainEnterprise.Id, deptData.Id, "POS-DATA", "数据管道工程师", level: "P6", description: "负责流式 ETL 与对账机制", order: 2);
        var posApiEng = new Position(_guidGenerator.Create(), mainEnterprise.Id, deptMiddleware.Id, "POS-API", "网关与消息工程师", level: "P6", description: "负责 API 路由与消息总线", order: 3);
        var posSre = new Position(_guidGenerator.Create(), mainEnterprise.Id, deptOps.Id, "POS-SRE", "平台可靠性工程师", level: "P6", description: "负责可观测性与调度监控", order: 4);

        var allPositions = new List<Position> { posLead, posDataEng, posApiEng, posSre };
        await _positionRepository.InsertManyAsync(allPositions, autoSave: true);

        var existingEmpCount = await _employeeRepository.GetCountAsync();
        if (existingEmpCount > 0)
        {
            return;
        }

        var empSpecs = new (string No, string Name, CompanyDepartment Dept, Position Pos, string Phone, string Email)[]
        {
            ("SM001", "林远舟", deptPlatform, posLead, "13800001001", "lin.yuanzhou@example.test"),
            ("SM002", "陈知夏", deptData, posDataEng, "13800001002", "chen.zhixia@example.test"),
            ("SM003", "周牧云", deptMiddleware, posApiEng, "13800001003", "zhou.muyun@example.test"),
            ("SM004", "许清禾", deptOps, posSre, "13800001004", "xu.qinghe@example.test"),
        };

        var employees = new List<Employee>();
        foreach (var spec in empSpecs)
        {
            var emp = new Employee(
                _guidGenerator.Create(),
                mainEnterprise.Id,
                spec.No,
                spec.Name,
                departmentId: spec.Dept.Id,
                email: spec.Email,
                phone: spec.Phone,
                position: spec.Pos.Name,
                positionId: spec.Pos.Id);
            employees.Add(emp);
        }

        await _employeeRepository.InsertManyAsync(employees, autoSave: true);

        deptPlatform.SetLeader(employees[0].Id);
        deptOps.SetLeader(employees[3].Id);
        await _departmentRepository.UpdateManyAsync([deptPlatform, deptOps], autoSave: true);
    }
}
