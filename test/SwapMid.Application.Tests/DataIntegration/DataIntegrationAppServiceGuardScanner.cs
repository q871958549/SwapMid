using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SwapMid.DataIntegration;

internal static class DataIntegrationAppServiceGuardScanner
{
    public static string[] ValidateServiceSet(
        IEnumerable<DataIntegrationSourceDocument> sources,
        IReadOnlyCollection<DataIntegrationServiceContract> contracts)
    {
        var violations = new List<string>();
        var actual = new List<string>();
        foreach (var source in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Source);
            var errors = tree.GetDiagnostics()
                .Where(x => x.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length != 0)
            {
                violations.Add(
                    $"{source.FileName} 源码无法解析：" +
                    string.Join(" | ", errors.Select(x => x.ToString())));
                continue;
            }

            actual.AddRange(tree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(IsApplicationServiceCandidate)
                .Select(declaration => ServiceKey(
                    source.FileName,
                    declaration.Identifier.ValueText)));
        }

        var orderedActual = actual
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var expected = contracts
            .Select(x => ServiceKey(x.FileName, x.ClassName))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var duplicate in orderedActual
                     .GroupBy(x => x, StringComparer.Ordinal)
                     .Where(group => group.Count() != 1))
        {
            violations.Add($"DataIntegration 应用服务类声明数量不是 1：{duplicate.Key}");
        }

        foreach (var duplicate in expected
                     .GroupBy(x => x, StringComparer.Ordinal)
                     .Where(group => group.Count() != 1))
        {
            violations.Add($"契约重复登记应用服务：{duplicate.Key}");
        }

        foreach (var unexpected in orderedActual.Except(expected, StringComparer.Ordinal))
        {
            violations.Add($"DataIntegration 存在未登记的应用服务：{unexpected}");
        }

        foreach (var missing in expected.Except(orderedActual, StringComparer.Ordinal))
        {
            violations.Add($"DataIntegration 契约登记的应用服务缺失：{missing}");
        }

        return violations.ToArray();
    }

    public static string[] ValidateHostGuards(
        string className,
        string source,
        string classAuthorizePermission,
        IReadOnlyCollection<DataIntegrationEntryContract> expectedEntries)
    {
        var violations = new List<string>();
        var classDeclaration = ParseClass(className, source, violations);
        if (classDeclaration == null)
        {
            return violations.ToArray();
        }

        if (classDeclaration.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)))
        {
            violations.Add($"{className} 禁止 partial，避免其他文件追加未扫描的 public 入口");
        }

        if (HasAllowAnonymousAttribute(classDeclaration.AttributeLists))
        {
            violations.Add($"{className} 禁止 AllowAnonymous");
        }

        if (!HasExactAuthorize(
                classDeclaration.AttributeLists,
                classAuthorizePermission))
        {
            violations.Add(
                $"{className} 类级 Authorize 必须且只能是 {classAuthorizePermission}");
        }

        foreach (var member in classDeclaration.Members.Where(member =>
                     member.DescendantNodesAndSelf()
                         .OfType<ExplicitInterfaceSpecifierSyntax>()
                         .Any()))
        {
            violations.Add(
                $"{className} 禁止显式接口成员绕过 public 入口闭集：{Canonical(member)}");
        }

        var expectedBySignature = expectedEntries.ToDictionary(x => x.Signature);
        var publicMethods = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(IsPublic)
            .ToArray();
        var actualSignatures = publicMethods.Select(GetSignature).ToArray();

        foreach (var unexpected in actualSignatures.Except(expectedBySignature.Keys))
        {
            violations.Add($"{className}.{unexpected} 是未登记的 public declared 入口");
        }

        foreach (var missing in expectedBySignature.Keys.Except(actualSignatures))
        {
            violations.Add($"{className}.{missing} 已从 public declared 入口消失");
        }

        foreach (var method in publicMethods)
        {
            var signature = GetSignature(method);
            if (!expectedBySignature.TryGetValue(signature, out var expected))
            {
                continue;
            }

            if (HasAllowAnonymousAttribute(method.AttributeLists))
            {
                violations.Add($"{className}.{signature} 禁止 AllowAnonymous");
            }

            if (!HasExactAuthorize(
                    method.AttributeLists,
                    expected.AuthorizePermission))
            {
                violations.Add(
                    $"{className}.{signature} 方法级 Authorize 必须精确为 " +
                    (expected.AuthorizePermission ?? "<none>"));
            }

            if (method.ExpressionBody != null || method.Body == null)
            {
                violations.Add($"{className}.{signature} host-only 门禁要求块方法体，禁止表达式体");
                continue;
            }

            var firstStatement = method.Body.Statements.FirstOrDefault();
            if (!IsExactHostGuard(firstStatement, expected.Permission))
            {
                violations.Add(
                    $"{className}.{signature} 首个顶层 statement 必须是精确 host-only 门禁 " +
                    $"global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(" +
                    $"base.CurrentTenant.Id, {expected.Permission})；" +
                    $"实际：{(firstStatement == null ? "<missing>" : Canonical(firstStatement))}");
            }
        }

        return violations.ToArray();
    }

    public static string[] ValidateCrudContract(
        string className,
        string source,
        IReadOnlyCollection<DataIntegrationEntryContract> crudEntries,
        IReadOnlyCollection<DataIntegrationPolicyAssignmentContract> policyAssignments)
    {
        var violations = new List<string>();
        var classDeclaration = ParseClass(className, source, violations);
        if (classDeclaration == null)
        {
            return violations.ToArray();
        }

        var methodsBySignature = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(IsPublic)
            .ToDictionary(GetSignature);
        foreach (var expected in crudEntries)
        {
            if (!methodsBySignature.TryGetValue(expected.Signature, out var method))
            {
                violations.Add($"{className}.{expected.Signature} CRUD 入口缺失");
                continue;
            }

            if (!HasExactAuthorize(
                    method.AttributeLists,
                    expected.AuthorizePermission))
            {
                violations.Add(
                    $"{className}.{expected.Signature} Authorize 必须且只能是 {expected.AuthorizePermission}");
            }

            var secondStatement = method.Body?.Statements.Skip(1).FirstOrDefault();
            if (!IsExactAwaitedPolicyCheck(secondStatement, expected.PolicyCheck!))
            {
                violations.Add(
                    $"{className}.{expected.Signature} 第二个顶层 statement 必须是直接策略检查 {expected.PolicyCheck}");
            }
        }

        var constructors = classDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToArray();
        if (constructors.Length != 1 ||
            !IsPublic(constructors[0]) ||
            !HasExactPolicyAssignments(constructors[0], policyAssignments))
        {
            violations.Add(
                $"{className} 构造器 PolicyName 集合必须且只能按契约顶层直接赋值");
        }

        return violations.ToArray();
    }

    private static ClassDeclarationSyntax? ParseClass(
        string className,
        string source,
        ICollection<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            violations.Add($"{className} 源码无法解析：{string.Join(" | ", errors.Select(x => x.ToString()))}");
            return null;
        }

        var classes = tree.GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText == className)
            .ToArray();
        if (classes.Length != 1)
        {
            violations.Add($"{className} 类声明数量应为 1，实际为 {classes.Length}");
            return null;
        }

        return classes[0];
    }

    private static bool IsExactHostGuard(StatementSyntax? statement, string permission)
        => statement is ExpressionStatementSyntax
        {
            Expression: InvocationExpressionSyntax invocation,
        } &&
        Canonical(invocation.Expression) ==
        "global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext" &&
        invocation.ArgumentList.Arguments.Count == 2 &&
        Canonical(invocation.ArgumentList.Arguments[0].Expression) ==
        "base.CurrentTenant.Id" &&
        Canonical(invocation.ArgumentList.Arguments[1].Expression) == permission;

    private static bool IsExactAwaitedPolicyCheck(
        StatementSyntax? statement,
        string policyCheck)
        => statement is ExpressionStatementSyntax
        {
            Expression: AwaitExpressionSyntax
            {
                Expression: InvocationExpressionSyntax invocation,
            },
        } &&
        Canonical(invocation.Expression) == policyCheck &&
        invocation.ArgumentList.Arguments.Count == 0;

    private static bool HasExactPolicyAssignments(
        ConstructorDeclarationSyntax constructor,
        IReadOnlyCollection<DataIntegrationPolicyAssignmentContract> expected)
    {
        var expectedByProperty = expected
            .GroupBy(item => item.PropertyName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (expectedByProperty.Values.Any(items => items.Length != 1))
        {
            return false;
        }

        var assignments = constructor.Body?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                Canonical(assignment.Left).EndsWith("PolicyName", StringComparison.Ordinal))
            .ToArray() ?? Array.Empty<AssignmentExpressionSyntax>();
        if (assignments.Length != expected.Count)
        {
            return false;
        }

        return assignments.All(assignment =>
        {
            var propertyName = Canonical(assignment.Left);
            return expectedByProperty.TryGetValue(propertyName, out var matches) &&
                   assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                   assignment.Parent is ExpressionStatementSyntax expressionStatement &&
                   expressionStatement.Parent == constructor.Body &&
                   Canonical(assignment.Right) == matches[0].Permission;
        });
    }

    private static bool HasAllowAnonymousAttribute(
        SyntaxList<AttributeListSyntax> attributeLists)
        => attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name switch
            {
                IdentifierNameSyntax identifier =>
                    identifier.Identifier.ValueText is "AllowAnonymous" or "AllowAnonymousAttribute",
                QualifiedNameSyntax qualified =>
                    qualified.Right.Identifier.ValueText is "AllowAnonymous" or "AllowAnonymousAttribute",
                AliasQualifiedNameSyntax alias =>
                    alias.Name.Identifier.ValueText is "AllowAnonymous" or "AllowAnonymousAttribute",
                _ => false,
            });

    private static bool HasExactAuthorize(
        SyntaxList<AttributeListSyntax> attributeLists,
        string? expectedPermission)
    {
        var attributes = attributeLists
            .SelectMany(list => list.Attributes)
            .Where(HasAuthorizeTerminalName)
            .ToArray();
        if (expectedPermission == null)
        {
            return attributes.Length == 0;
        }

        return attributes.Length == 1 &&
               Canonical(attributes[0].Name) == "Authorize" &&
               attributes[0].ArgumentList?.Arguments.Count == 1 &&
               Canonical(attributes[0].ArgumentList!.Arguments[0].Expression) ==
               expectedPermission;
    }

    private static bool HasAuthorizeTerminalName(AttributeSyntax attribute)
        => attribute.Name switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText is "Authorize" or "AuthorizeAttribute",
            QualifiedNameSyntax qualified =>
                qualified.Right.Identifier.ValueText is "Authorize" or "AuthorizeAttribute",
            AliasQualifiedNameSyntax alias =>
                alias.Name.Identifier.ValueText is "Authorize" or "AuthorizeAttribute",
            _ => false,
        };

    private static bool IsPublic(MemberDeclarationSyntax member)
        => member.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword));

    private static bool IsApplicationServiceCandidate(
        ClassDeclarationSyntax declaration)
        => declaration.Identifier.ValueText.EndsWith("AppService", StringComparison.Ordinal) ||
           declaration.BaseList?.Types.Any(baseType =>
               Canonical(baseType.Type).Contains("AppService", StringComparison.Ordinal) ||
               Canonical(baseType.Type).Contains("ApplicationService", StringComparison.Ordinal)) == true;

    private static string ServiceKey(string fileName, string className)
        => fileName + "::" + className;

    private static string GetSignature(MethodDeclarationSyntax method)
        => method.Identifier.ValueText + "(" +
           string.Join(",", method.ParameterList.Parameters
               .Select(parameter => Canonical(parameter.Type!))) + ")";

    private static string Canonical(SyntaxNode node)
        => string.Concat(node.DescendantTokens().Select(token => token.Text));
}

internal sealed record DataIntegrationEntryContract(
    string Signature,
    string Permission,
    string? AuthorizePermission = null,
    string? PolicyCheck = null);

internal sealed record DataIntegrationServiceContract(
    string FileName,
    string ClassName,
    string ClassAuthorizePermission,
    DataIntegrationEntryContract[] Entries,
    DataIntegrationPolicyAssignmentContract[] PolicyAssignments);

internal sealed record DataIntegrationPolicyAssignmentContract(
    string PropertyName,
    string Permission);

internal sealed record DataIntegrationSourceDocument(
    string FileName,
    string Source);
