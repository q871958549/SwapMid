using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Controllers.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class TargetReceiptHttpContractTests
{
    [Fact]
    public void Controller双重载必须独立授权禁用Uow且仅Action接收请求取消()
    {
        var methods = typeof(PipelineExecutionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == nameof(
                    PipelineExecutionController.VerifyTargetReceiptAsync))
            .ToArray();
        methods.Length.ShouldBe(2);

        foreach (var method in methods)
        {
            method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Select(attribute => attribute.Policy)
                .ShouldContain(
                    SwapMidPermissions.DataIntegration
                        .PipelineExecutions.VerifyTargetReceipt,
                    "Controller 每个可调用重载都必须显式钉住机器核验权限");
            var unitOfWork = method.GetCustomAttribute<UnitOfWorkAttribute>(
                inherit: true);
            unitOfWork.ShouldNotBeNull();
            unitOfWork!.IsDisabled.ShouldBeTrue();
        }

        var action = methods.Single(method =>
            method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)));
        action.GetCustomAttribute<HttpPostAttribute>()!
            .Template.ShouldBe("{id}/verify-target-receipt");
        action.GetCustomAttribute<NonActionAttribute>().ShouldBeNull();
        action.GetParameters()[^1].ParameterType.ShouldBe(
            typeof(CancellationToken));
        action.GetParameters()[^1].HasDefaultValue.ShouldBeTrue();

        var compatibilityOverload = methods.Single(method =>
            method != action);
        compatibilityOverload.GetCustomAttribute<NonActionAttribute>()
            .ShouldNotBeNull();
        compatibilityOverload.GetCustomAttribute<HttpPostAttribute>()
            .ShouldBeNull();
        compatibilityOverload.GetParameters().Length.ShouldBe(1);
    }

    [Fact]
    public async Task ControllerAction必须原样转发请求CancellationToken()
    {
        var service = Substitute.For<IPipelineExecutionAppService>();
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var expected = new PipelineExecutionDto { Id = id };
        service.VerifyTargetReceiptAsync(id, cancellation.Token)
            .Returns(Task.FromResult(expected));
        var controller = new PipelineExecutionController(service);

        var actual = await controller.VerifyTargetReceiptAsync(
            id,
            cancellation.Token);

        actual.ShouldBeSameAs(expected);
        await service.Received(1).VerifyTargetReceiptAsync(
            id,
            cancellation.Token);
        await service.DidNotReceive().VerifyTargetReceiptAsync(id);
    }
}
