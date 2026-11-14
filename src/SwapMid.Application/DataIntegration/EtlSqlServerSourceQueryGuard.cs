using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SwapMid.DataIntegration;

/// <summary>
/// SQL Server 源查询的 AST 边界。关键词扫描无法识别省略分号的第二条 T-SQL 语句，
/// 因而必须先由 ScriptDom 证明整个输入只有一个批次、一个 SelectStatement。
/// </summary>
internal static class EtlSqlServerSourceQueryGuard
{
    internal static bool IsSafeSingleSelect(string sourceQuery)
    {
        var parser = new TSql180Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sourceQuery);
        var fragment = parser.Parse(reader, out var errors);

        if (errors.Count != 0 ||
            fragment is not TSqlScript { Batches.Count: 1 } script ||
            script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not SelectStatement select ||
            select.Into is not null ||
            select.OptimizerHints.Count != 0)
        {
            return false;
        }

        var visitor = new UnsafeSelectVisitor();
        select.Accept(visitor);
        return !visitor.IsUnsafe;
    }

    private sealed class UnsafeSelectVisitor : TSqlFragmentVisitor
    {
        internal bool IsUnsafe { get; private set; }

        public override void ExplicitVisit(TableHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(IndexTableHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(LiteralTableHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(ForceSeekTableHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(TableHintsOptimizerHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(OptimizerHint node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(NextValueForExpression node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(SelectSetVariable node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(VariableReference node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(GlobalVariableExpression node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(OpenQueryTableReference node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(OpenRowsetTableReference node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(BulkOpenRowset node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(InternalOpenRowset node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(OpenRowsetCosmos node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(AdHocTableReference node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(OpenXmlTableReference node) =>
            IsUnsafe = true;

        public override void ExplicitVisit(NamedTableReference node)
        {
            if (node.SchemaObject.ServerIdentifier is not null)
            {
                IsUnsafe = true;
                return;
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(
            SchemaObjectFunctionTableReference node)
        {
            if (node.SchemaObject.ServerIdentifier is not null)
            {
                IsUnsafe = true;
                return;
            }

            base.ExplicitVisit(node);
        }
    }
}
