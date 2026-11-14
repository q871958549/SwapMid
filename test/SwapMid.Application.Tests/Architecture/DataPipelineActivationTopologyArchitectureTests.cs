using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public sealed class DataPipelineActivationTopologyArchitectureTests
{
    [Fact]
    public void activation拓扑pin必须先于准备与外部探针并覆盖两段UoW()
    {
        var root = LocateRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SwapMid.Application",
            "DataIntegration",
            "DataPipelineActivationCoordinator.cs"));
        var syntaxRoot = CSharpSyntaxTree.ParseText(source).GetRoot();
        var coordinator = syntaxRoot.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText ==
                "DataPipelineActivationCoordinator");
        var activate = coordinator.Members
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ActivateAsync");
        var prepare = coordinator.Members
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "PrepareAsync");

        var hostScope = FindInvocation(activate, "Change");
        var capture = FindInvocation(activate, "CaptureSafeAsync");
        var enter = FindInvocation(activate, "EnterSafeScope");
        var prepareCall = FindInvocation(activate, "PrepareAsync");
        var targetProbe = FindInvocation(activate, "ProbeActivationAsync");
        var commitBegin = FindInvocation(activate, "Begin");
        var commitEnsure = FindInvocation(activate, "EnsureSafeScope");
        var lockedRead = FindInvocation(
            activate,
            "GetActivationForUpdateAsync");

        hostScope.SpanStart.ShouldBeLessThan(capture.SpanStart,
            "必须先进入 host tenant scope，再捕获 host Default");
        capture.SpanStart.ShouldBeLessThan(enter.SpanStart);
        enter.SpanStart.ShouldBeLessThan(prepareCall.SpanStart,
            "pin scope 必须覆盖首次平台 Prepare 读取");
        enter.SpanStart.ShouldBeLessThan(targetProbe.SpanStart,
            "外部目标探针必须在已固定的 topology scope 内运行");
        targetProbe.SpanStart.ShouldBeLessThan(commitBegin.SpanStart);
        commitBegin.SpanStart.ShouldBeLessThan(commitEnsure.SpanStart);
        commitEnsure.SpanStart.ShouldBeLessThan(lockedRead.SpanStart,
            "第二段事务必须在任何锁定读取前确认 pinned scope");

        var prepareBegin = FindInvocation(prepare, "Begin");
        var prepareEnsure = FindInvocation(prepare, "EnsureSafeScope");
        var firstPrepareRead = prepare.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => InvocationName(invocation) == "GetAsync" &&
                invocation.Expression.ToString().Contains(
                    "_pipelineRepository",
                    StringComparison.Ordinal));
        prepareBegin.SpanStart.ShouldBeLessThan(prepareEnsure.SpanStart);
        prepareEnsure.SpanStart.ShouldBeLessThan(firstPrepareRead.SpanStart,
            "第一段 UoW 必须在首次平台读取前确认 pinned scope");

        coordinator.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(invocation =>
                InvocationName(invocation) == "EnsureSafeScope")
            .ShouldBe(2, "激活两段 UoW 必须各有且仅有一次 scope 守卫");
        capture.ArgumentList.ToString().ShouldContain(
            "TargetReceiptActivation");
        enter.ArgumentList.ToString().ShouldContain(
            "TargetReceiptActivation");
        prepareEnsure.ArgumentList.ToString().ShouldContain(
            "TargetReceiptActivation");
        commitEnsure.ArgumentList.ToString().ShouldContain(
            "TargetReceiptActivation");

        var scopeVariable = activate.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText ==
                "terminalStoreScope");
        var scopeDeclaration = scopeVariable.FirstAncestorOrSelf<
            LocalDeclarationStatementSyntax>();
        scopeDeclaration.ShouldNotBeNull();
        scopeDeclaration!.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            .ShouldBeTrue("调用结束必须自动 Dispose 并恢复 topology scope");

        coordinator.Members
            .OfType<ConstructorDeclarationSyntax>()
            .Single()
            .ParameterList.Parameters
            .Count(parameter => parameter.Type?.ToString() ==
                "IPipelineTerminalStoreTopologyGuard")
            .ShouldBe(1, "构造 DI 必须显式且唯一注入 topology guard");
    }

    private static InvocationExpressionSyntax FindInvocation(
        SyntaxNode node,
        string name)
        => node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => InvocationName(invocation) == name);

    private static string? InvocationName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SwapMid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未能定位仓库根目录");
    }
}
