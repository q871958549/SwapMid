using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// ETL 异常出口抗回归：DataIntegration 不得把底层异常正文或异常对象直接送入持久化、
/// DTO、通知或普通日志。可诊断信息只能经 PipelineExecutionFailurePolicy 归一化。
/// </summary>
public class EtlFailureBoundaryGuardTests
{
    private static readonly HashSet<string> AllowedFailurePolicyMethods =
    [
        "Record",
        "DescribeFailure",
        "DescribeCoordinationFailure",
        "DescribeOutcomeUnknown",
    ];

    [Fact]
    public void DataIntegration不应读取异常正文或把异常对象交给日志()
    {
        var directory = LocateDataIntegrationDirectory();
        directory.ShouldNotBeNull(
            "未能定位 Application/DataIntegration 目录");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     directory!, "*.cs", SearchOption.AllDirectories))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                var unsafeReferences = FindUnsafeExceptionReferences(catchClause)
                    .Select(identifier =>
                        $"{Path.GetFileName(file)}:" +
                        $"{identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {identifier}");

                offenders.AddRange(unsafeReferences);
            }
        }

        offenders.ShouldBeEmpty(
            "ETL 底层异常可能含连接串、SQL 或密钥；只能记录 FailurePolicy 输出的错误码、" +
            "CorrelationId 与 ExceptionType：\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("BusinessException", "_logger.LogWarning(\"{Code}\", exception.Code);")]
    [InlineData("EtlTargetCommitOutcomeUnknownException", "Persist(exception.ReceiptEvidence);")]
    [InlineData("SqlException", "Persist(exception.Number);")]
    [InlineData("MySqlException", "Persist(exception.Number);")]
    public void 低敏分类字段离开限定判别与未知态收敛位置仍应被拦截(
        string exceptionType,
        string statement)
    {
        var catchClause = ParseCatchClause(
            $"catch ({exceptionType} exception) {{ {statement} }}");

        FindUnsafeExceptionReferences(catchClause).Count().ShouldBe(1);
    }

    [Theory]
    [InlineData("catch (BusinessException exception) when (exception.Code == ExpectedCode) { throw; }")]
    [InlineData("catch (SqlException exception) when (exception.Number == 547) { throw; }")]
    [InlineData("catch (MySqlException exception) when (exception.Number is 3819 or 4025) { throw; }")]
    [InlineData("catch (EtlTargetCommitOutcomeUnknownException exception) { return FinalizeAsync(id, TerminalKind.OutcomeUnknown, null, failure, exception.ReceiptEvidence); }")]
    public void 仅允许直接分类与OutcomeUnknown证据收敛(string catchText)
    {
        FindUnsafeExceptionReferences(ParseCatchClause(catchText))
            .ShouldBeEmpty();
    }

    private static IEnumerable<IdentifierNameSyntax>
        FindUnsafeExceptionReferences(CatchClauseSyntax catchClause)
    {
        var exceptionName = catchClause.Declaration?.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(exceptionName))
        {
            return [];
        }

        return catchClause.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == exceptionName)
            .Where(identifier => !IsAllowedExceptionUse(identifier, catchClause));
    }

    private static bool IsAllowedExceptionUse(
        IdentifierNameSyntax identifier,
        CatchClauseSyntax catchClause)
    {
        // 允许读取异常类型，但禁止沿异常对象继续读取 Message、StackTrace、Data、
        // InnerException，或通过别名、插值、对象封装、任意 helper 将其送出边界。
        if (identifier.Parent is MemberAccessExpressionSyntax
            {
                Expression: var expression,
                Name.Identifier.ValueText: "GetType",
                Parent: InvocationExpressionSyntax
            } &&
            ReferenceEquals(expression, identifier))
        {
            return true;
        }

        if (identifier.Parent is MemberAccessExpressionSyntax
            {
                Expression: var memberExpression,
                Name.Identifier.ValueText: var memberName,
            } memberAccess &&
            ReferenceEquals(memberExpression, identifier) &&
            IsAllowedClassificationMember(
                catchClause.Declaration?.Type.ToString(),
                memberName,
                memberAccess,
                catchClause))
        {
            return true;
        }

        return identifier.Parent is ArgumentSyntax
        {
            Expression: var argumentExpression,
            Parent.Parent: InvocationExpressionSyntax invocation
        } &&
               ReferenceEquals(argumentExpression, identifier) &&
               IsAllowedFailurePolicyCall(invocation);
    }

    private static bool IsAllowedClassificationMember(
        string? exceptionType,
        string memberName,
        MemberAccessExpressionSyntax memberAccess,
        CatchClauseSyntax catchClause) =>
        (exceptionType, memberName) switch
        {
            ("BusinessException", "Code") =>
                IsDirectCatchFilterClassification(memberAccess, catchClause),
            ("SqlException", "Number") =>
                IsDirectCatchFilterClassification(memberAccess, catchClause),
            ("PostgresException", "SqlState") =>
                IsDirectCatchFilterClassification(memberAccess, catchClause),
            ("MySqlException", "Number") =>
                IsDirectCatchFilterClassification(memberAccess, catchClause),
            ("EtlTargetCommitOutcomeUnknownException", "ReceiptEvidence") =>
                IsOutcomeUnknownFinalizeArgument(memberAccess),
            _ => false,
        };

    private static bool IsDirectCatchFilterClassification(
        MemberAccessExpressionSyntax memberAccess,
        CatchClauseSyntax catchClause)
    {
        var filter = catchClause.Filter?.FilterExpression;
        if (filter == null || !filter.Span.Contains(memberAccess.Span))
        {
            return false;
        }

        return memberAccess.Parent is BinaryExpressionSyntax
                   { RawKind: (int)SyntaxKind.EqualsExpression } ||
               memberAccess.Parent is IsPatternExpressionSyntax;
    }

    private static bool IsOutcomeUnknownFinalizeArgument(
        MemberAccessExpressionSyntax memberAccess) =>
        memberAccess.Parent is ArgumentSyntax
        {
            Parent.Parent: InvocationExpressionSyntax invocation,
        } &&
        invocation.Expression is IdentifierNameSyntax
        {
            Identifier.ValueText: "FinalizeAsync",
        } &&
        invocation.ArgumentList.Arguments.Any(argument =>
            argument.Expression.ToString() == "TerminalKind.OutcomeUnknown");

    private static bool IsAllowedFailurePolicyCall(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax member &&
        member.Expression is IdentifierNameSyntax identifier &&
        identifier.Identifier.ValueText == "_failurePolicy" &&
        AllowedFailurePolicyMethods.Contains(member.Name.Identifier.ValueText);

    private static CatchClauseSyntax ParseCatchClause(string catchText) =>
        CSharpSyntaxTree.ParseText(
                $"class GuardFixture {{ object Method() {{ try {{ }} {catchText} }} }}")
            .GetRoot()
            .DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Single();

    private static string? LocateDataIntegrationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwapMid.slnx")))
            {
                var target = Path.Combine(
                    directory.FullName,
                    "src",
                    "SwapMid.Application",
                    "DataIntegration");
                return Directory.Exists(target) ? target : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
