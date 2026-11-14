using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// 约定注册可解析性守卫：Application 程序集内自定义接口只要被构造函数注入，就必须真的有实现把它
/// 暴露为服务类型，否则运行时才会以 Autofac <c>DependencyResolutionException</c> 炸在调用路径上
/// （编译通过、启动通过、单元测试用替身也通过）。
///
/// 背景（2026-07-29 生产事故）：<c>RecruitmentClamAvAttachmentScanner</c> 实现
/// <c>IRecruitmentAttachmentMalwareScanner</c> 并标了 <c>ITransientDependency</c>，但 ABP
/// <c>DefaultConventionalRegistrar</c> 的默认接口暴露规则是「类名以（接口名去掉前导 I）结尾」——
/// <c>RecruitmentClamAvAttachmentScanner</c> 不以 <c>RecruitmentAttachmentMalwareScanner</c> 结尾，
/// 于是只注册了具体类自身，接口从未进容器。招聘渠道工作单补偿定时任务解析
/// <c>RecruitmentChannelAttachmentStore</c> 时持续失败重试。修复方式是显式
/// <c>[ExposeServices(typeof(接口), typeof(自身))]</c>，与 Scheduling 域 Handler 的双列写法一致。
/// </summary>
public class ConventionalRegistrationGuardTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(SwapMidApplicationModule).Assembly;

    /// <summary>
    /// 实现落在 Application 程序集之外（Host / EF Core / 测试宿主装配）的接口，按名单豁免。
    /// 新增豁免必须写明由哪个模块负责注册，不得用来掩盖忘记暴露服务类型。
    /// </summary>
    private static readonly HashSet<string> ExternallyRegisteredInterfaces = new(StringComparer.Ordinal);

    [Fact]
    public void 被构造函数注入的应用层接口必须有实现把它暴露为服务类型()
    {
        var exposedServices = new HashSet<Type>(RunConventionalRegistrar()
            .Select(service => service.ServiceType));

        var injectedInterfaces = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(IsConventionallyRegistered)
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => UnwrapCollection(parameter.ParameterType))
            .Where(IsOwnApplicationInterface)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var unresolvable = injectedInterfaces
            .Where(contract => !exposedServices.Contains(contract))
            .Where(contract => !ExternallyRegisteredInterfaces.Contains(contract.FullName!))
            .Select(contract => contract.FullName!)
            .ToList();

        unresolvable.ShouldBeEmpty(
            "以下接口被构造函数注入但没有任何实现把它暴露为服务类型，运行时解析必炸（多见于实现类名与" +
            "接口名不满足 ABP 默认约定，需显式 [ExposeServices]）：" + string.Join("; ", unresolvable));
    }

    /// <summary>
    /// 用 ABP 自己的约定注册器跑一遍 Application 程序集，得到运行时真正会进容器的服务描述符——
    /// 不复刻默认接口暴露规则，避免守卫本身与框架行为漂移。
    /// </summary>
    private static IReadOnlyList<ServiceDescriptor> RunConventionalRegistrar()
    {
        var services = new ServiceCollection();
        new DefaultConventionalRegistrar().AddAssembly(services, ApplicationAssembly);
        return services.ToList();
    }

    private static bool IsConventionallyRegistered(Type type)
    {
        if (type.IsDefined(typeof(DisableConventionalRegistrationAttribute), inherit: true))
        {
            return false;
        }

        return typeof(ITransientDependency).IsAssignableFrom(type) ||
               typeof(IScopedDependency).IsAssignableFrom(type) ||
               typeof(ISingletonDependency).IsAssignableFrom(type) ||
               typeof(IApplicationService).IsAssignableFrom(type) ||
               type.IsDefined(typeof(DependencyAttribute), inherit: true) ||
               type.IsDefined(typeof(ExposeServicesAttribute), inherit: true);
    }

    private static Type UnwrapCollection(Type parameterType)
    {
        if (parameterType.IsGenericType &&
            parameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return parameterType.GetGenericArguments()[0];
        }

        return parameterType;
    }

    private static bool IsOwnApplicationInterface(Type type)
    {
        return type.IsInterface &&
               !type.IsGenericType &&
               type.Assembly == ApplicationAssembly;
    }
}
