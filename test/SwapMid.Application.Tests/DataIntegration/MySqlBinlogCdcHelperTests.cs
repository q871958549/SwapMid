using System;
using System.Collections.Generic;
using System.Text;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class MySqlBinlogCdcHelperTests
{
    [Fact]
    public void Checkpoint_Parsing_And_Validation_Should_Handle_Valid_And_Invalid_Formats()
    {
        var valid = MySqlBinlogCdcCaptureService.ValidateCheckpoint("binlog.000012:4096");
        valid.Filename.ShouldBe("binlog.000012");
        valid.Position.ShouldBe(4096);
        valid.Token.ShouldBe("binlog.000012:4096");

        var longName = MySqlBinlogCdcCaptureService.ValidateCheckpoint("mysql-bin-changelog.000123:4294967295");
        longName.Token.ShouldBe("mysql-bin-changelog.000123:4294967295");

        MySqlBinlogCdcCaptureService.TryParseCheckpoint("binlog.000001:3").ShouldBeNull();
        MySqlBinlogCdcCaptureService.TryParseCheckpoint("../binlog.000001:120").ShouldBeNull();
        MySqlBinlogCdcCaptureService.TryParseCheckpoint("missing_colon").ShouldBeNull();

        Should.Throw<BusinessException>(() =>
            CdcStreamMappingHelpers.NormalizeCheckpointResetToken(CdcSourceType.MySqlBinlog, "invalid"));
    }

    [Fact]
    public void ComputeBinlogLagBytes_Should_Sum_Across_Multiple_Files_And_Detect_Purged_Checkpoint()
    {
        var logs = new List<MySqlBinlogCdcCaptureService.MySqlBinlogFileInfo>
        {
            new("binlog.000001", 1000),
            new("binlog.000002", 2500),
            new("binlog.000003", 1200),
        };

        var sameFileLag = MySqlBinlogCdcCaptureService.ComputeBinlogLagBytes(
            logs, "binlog.000003", 200, "binlog.000003", 950, out var foundSame);
        foundSame.ShouldBeTrue();
        sameFileLag.ShouldBe(750);

        var multiFileLag = MySqlBinlogCdcCaptureService.ComputeBinlogLagBytes(
            logs, "binlog.000001", 400, "binlog.000003", 800, out var foundMulti);
        foundMulti.ShouldBeTrue();
        multiFileLag.ShouldBe((1000 - 400) + 2500 + 800);

        var purgedLag = MySqlBinlogCdcCaptureService.ComputeBinlogLagBytes(
            logs, "binlog.000000", 500, "binlog.000003", 800, out var foundPurged);
        foundPurged.ShouldBeFalse();
        purgedLag.ShouldBeNull();
    }

    [Fact]
    public void MapRowCells_Should_Align_Sparse_ColumnsPresent_And_Decode_Textual_Bytes()
    {
        var columns = new List<MySqlBinlogCdcCaptureService.MySqlColumnMeta>
        {
            new("order_id", "bigint", false),
            new("customer", "varchar", false),
            new("payload", "json", true),
            new("raw_hash", "varbinary", true),
        };

        // Simulate binlog_row_image=MINIMAL where colIdx 1 ("customer") is absent from row image.
        var present = new[] { true, false, true, true };
        var cells = new object?[]
        {
            9001L,
            Encoding.UTF8.GetBytes("{\"ok\":true}"),
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
        };

        var row = MySqlBinlogCdcCaptureService.MapRowCells(cells, present, columns);

        row.Keys.ShouldNotContain("customer");
        row["order_id"].ShouldBe(9001L);
        row["payload"].ShouldBe("{\"ok\":true}");
        row["raw_hash"].ShouldBe(Convert.ToBase64String(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
    }

    [Fact]
    public void NormalizeLakehouseTableFormat_Should_Restrict_DeltaLake_To_ExternalSink()
    {
        CdcStreamMirrorConfigValidator
            .NormalizeLakehouseTableFormat(CdcTargetMode.ExternalSink, LakehouseTableFormat.DeltaLake)
            .ShouldBe(LakehouseTableFormat.DeltaLake);

        CdcStreamMirrorConfigValidator
            .NormalizeLakehouseTableFormat(CdcTargetMode.RelationalTable, LakehouseTableFormat.DeltaLake)
            .ShouldBe(LakehouseTableFormat.SwapMidManifest);
    }
}
