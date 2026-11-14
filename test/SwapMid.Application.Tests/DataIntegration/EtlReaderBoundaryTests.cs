using System.Data;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class EtlReaderBoundaryTests
{
    [Fact]
    public void 整次载荷预算_精确上限允许且多一字节拒绝()
    {
        var budget = new EtlPayloadBudget();

        budget.Add(EtlPipelineRunner.MaxTotalPayloadBytes - 1);
        budget.Add(1);
        budget.UsedBytes.ShouldBe(
            EtlPipelineRunner.MaxTotalPayloadBytes);

        var exception = Should.Throw<BusinessException>(() =>
            budget.Add(1));
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    [Fact]
    public void 极宽结果_受单元格和暂存批次数双重收紧()
    {
        EtlPipelineRunner.GetMaxRowsForShape(
                columnCount: 2,
                maxRowsPerBatch: 500)
            .ShouldBe(EtlPipelineRunner.MaxRows);

        EtlPipelineRunner.GetMaxRowsForShape(
                columnCount: 256,
                maxRowsPerBatch: 7)
            .ShouldBe(3_906);
    }

    [Fact]
    public void MySql无类型NULL投影_作为已知零载荷类型允许()
    {
        EtlReaderBoundary.IsKnownProviderNull(
                typeof(object),
                "NULL",
                new RelationalDataSourceConnection.Probe
                {
                    Provider = "mysql",
                })
            .ShouldBeTrue();
    }

    [Fact]
    public void 未知CLR类型_在读取首行前拒绝()
    {
        using var table = new DataTable();
        table.Columns.Add("payload", typeof(object));
        using var reader = table.CreateDataReader();

        var exception = Should.Throw<BusinessException>(() =>
            EtlReaderBoundary.ValidatePreReadSchema(
                reader,
                new RelationalDataSourceConnection.Probe
                {
                    Provider = "sqlserver",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    [Fact]
    public void MySql声明列宽超过单值上限_在读取首行前拒绝()
    {
        using var table = new DataTable();
        var column = table.Columns.Add("payload", typeof(string));
        column.MaxLength = EtlPipelineRunner.MaxCellPayloadBytes / 4 + 1;
        using var reader = table.CreateDataReader();

        var exception = Should.Throw<BusinessException>(() =>
            EtlReaderBoundary.ValidatePreReadSchema(
                reader,
                new RelationalDataSourceConnection.Probe
                {
                    Provider = "mysql",
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }

    [Fact]
    public void MySql有限宽度基础类型_允许()
    {
        using var table = new DataTable();
        var text = table.Columns.Add("text_value", typeof(string));
        text.MaxLength = 100;
        table.Columns.Add("number_value", typeof(int));
        using var reader = table.CreateDataReader();

        Should.NotThrow(() =>
            EtlReaderBoundary.ValidatePreReadSchema(
                reader,
                new RelationalDataSourceConnection.Probe
                {
                    Provider = "mysql",
                }));
    }
}
