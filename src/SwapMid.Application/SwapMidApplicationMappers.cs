using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.EnterpriseManagement;
using SwapMid.MessageBus;
using Riok.Mapperly.Abstractions;

namespace SwapMid;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SwapMidApplicationMappers
{
    // API Management
    public static partial ApiGroupDto ToDto(this ApiGroup entity);
    [MapperIgnoreTarget(nameof(ApiRouteDto.GroupName))]
    public static partial ApiRouteDto ToDto(this ApiRoute entity);

    // Data Integration
    [MapperIgnoreTarget(nameof(DataSourceDto.Config))]
    [MapperIgnoreTarget(nameof(DataSourceDto.HasSecret))]
    [MapperIgnoreSource(nameof(DataSource.Config))]
    public static partial DataSourceDto ToDto(this DataSource entity);
    [MapperIgnoreTarget(nameof(DataPipelineDto.SourceName))]
    [MapperIgnoreTarget(nameof(DataPipelineDto.TargetName))]
    public static partial DataPipelineDto ToDto(this DataPipeline entity);

    // Enterprise Management
    public static partial EnterpriseDto ToDto(this Enterprise entity);
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.EnterpriseCode))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.EnterpriseName))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.Enterprise))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.ParentCode))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.ParentName))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.Parent))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.LeaderEmployeeNo))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.LeaderEmployeeName))]
    [MapperIgnoreTarget(nameof(CompanyDepartmentDto.LeaderEmployee))]
    public static partial CompanyDepartmentDto ToDto(this CompanyDepartment entity);

    [MapperIgnoreTarget(nameof(EmployeeDto.EnterpriseCode))]
    [MapperIgnoreTarget(nameof(EmployeeDto.EnterpriseName))]
    [MapperIgnoreTarget(nameof(EmployeeDto.Enterprise))]
    [MapperIgnoreTarget(nameof(EmployeeDto.DepartmentCode))]
    [MapperIgnoreTarget(nameof(EmployeeDto.DepartmentName))]
    [MapperIgnoreTarget(nameof(EmployeeDto.Department))]
    [MapperIgnoreTarget(nameof(EmployeeDto.PositionCode))]
    [MapperIgnoreTarget(nameof(EmployeeDto.PositionName))]
    [MapperIgnoreTarget(nameof(EmployeeDto.PositionRef))]
    public static partial EmployeeDto ToDto(this Employee entity);

    [MapperIgnoreTarget(nameof(PositionDto.EnterpriseCode))]
    [MapperIgnoreTarget(nameof(PositionDto.EnterpriseName))]
    [MapperIgnoreTarget(nameof(PositionDto.Enterprise))]
    [MapperIgnoreTarget(nameof(PositionDto.DepartmentCode))]
    [MapperIgnoreTarget(nameof(PositionDto.DepartmentName))]
    [MapperIgnoreTarget(nameof(PositionDto.Department))]
    public static partial PositionDto ToDto(this Position entity);

    // Message Bus
    public static partial MessageTopicDto ToDto(this MessageTopic entity);
    [MapperIgnoreTarget(nameof(MessageSubscriptionDto.TopicName))]
    [MapperIgnoreTarget(nameof(MessageSubscriptionDto.HasSecret))]
    [MapperIgnoreSource(nameof(MessageSubscription.Secret))]
    public static partial MessageSubscriptionDto ToDto(this MessageSubscription entity);
}
