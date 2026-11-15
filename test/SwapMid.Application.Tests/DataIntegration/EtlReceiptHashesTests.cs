using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// Target Receipt v1 哈希协议金向量。常量由独立 TLV/SHA-256 实现生成，锁定 domain、
/// RFC4122 GUID 字节序、整数大端序和值类型标签，避免两端“同时改错”仍自洽。
/// </summary>
public sealed class EtlReceiptHashesTests
{
    [Fact]
    public void Target_Resource_Execution_Contract_And_Schema_Must_Match_Golden_Vectors()
    {
        var instanceId = Guid.Parse(
            "00112233-4455-6677-8899-aabbccddeeff");
        var coordinationIdentityHash =
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "SWAPMID-SQL\\MSSQLLOCALDB",
                "SWAPMID_TARGET");

        coordinationIdentityHash.ShouldBe(
            "115CC004209673B96FC5EC68DA25872013D3AFF4DC4FB91C1B2AEFD24D1136B3");

        EtlReceiptHashes.ComputeTargetResource(
                "sqlserver",
                instanceId,
                "dbo.Target")
            .ShouldBe(
                "CD4D607CEA1CE6D622CBBAA8BE52E519695330F58A097C0E64EAFF05D54CDE3D");

        EtlReceiptHashes.ComputeExecutionContract(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                "0123456789abcdef0123456789abcdef",
                Guid.Parse("99999999-8888-7777-6666-555555555555"),
                "fedcba9876543210fedcba9876543210",
                "sqlserver",
                instanceId,
                coordinationIdentityHash,
                new string('A', 64),
                new string('B', 64),
                "dbo.Target",
                EtlWriteMode.Append,
                new string('C', 64))
            .ShouldBe(
                "E4769EB63580373406BF2190AB14A2DC21BF5361A0AE13C833B4B09F63935FDF");

        EtlReceiptHashes.ComputeSchemaHash(
                "Id:int:notnull|Text:nvarchar(32):null")
            .ShouldBe(
                "22CAFA604EB61F06CC1BC5E68236DFD323AAE99E5E86091C7C057C3775087866");
    }

    [Fact]
    public void Coordination_Identity_Must_Be_Sensitive_To_Provider_Count_Order_And_Every_Part()
    {
        var baseline = EtlReceiptHashes.ComputeCoordinationIdentity(
            "sqlserver",
            "SWAPMID-SQL\\MSSQLLOCALDB",
            "SWAPMID_TARGET");
        var variants = new[]
        {
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "mysql",
                "SWAPMID-SQL\\MSSQLLOCALDB",
                "SWAPMID_TARGET"),
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "SWAPMID-SQL\\MSSQLLOCALDB|SWAPMID_TARGET"),
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "SWAPMID_TARGET",
                "SWAPMID-SQL\\MSSQLLOCALDB"),
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "SWAPMID-SQL-2\\MSSQLLOCALDB",
                "SWAPMID_TARGET"),
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "SWAPMID-SQL\\MSSQLLOCALDB",
                "SWAPMID_TARGET_2"),
            EtlReceiptHashes.ComputeCoordinationIdentity(
                "sqlserver",
                "swapmid-sql\\mssqllocaldb",
                "SWAPMID_TARGET"),
        };

        baseline.Length.ShouldBe(PipelineExecutionConsts.Sha256HexLength);
        variants.ShouldAllBe(hash =>
            hash.Length == PipelineExecutionConsts.Sha256HexLength);
        variants.ShouldAllBe(hash => hash != baseline);
        variants.Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(variants.Length,
                "identity part 的边界、顺序和值必须全部进入 TLV 协议");

        ComputeExecutionContract(baseline).ShouldNotBe(
            ComputeExecutionContract(variants[3]),
            "coordination identity 必须进入 execution contract");
    }

    [Fact]
    public void Batch_Digest_Must_Match_All_Supported_Value_Type_Golden_Vector()
    {
        var columns = new[]
        {
            "Null", "Bool", "Byte", "SByte", "Short", "UShort",
            "Int", "UInt", "Long", "ULong", "Decimal", "Float",
            "Double", "Text", "Bytes", "Guid", "Date", "Offset",
            "Span",
        };
        var first = new object?[]
        {
            null,
            true,
            byte.MaxValue,
            sbyte.MinValue,
            short.MinValue,
            ushort.MaxValue,
            42,
            4_000_000_000U,
            long.MinValue,
            ulong.MaxValue,
            123.45m,
            -0.0f,
            BitConverter.Int64BitsToDouble(
                unchecked((long)0x7ff8000000000001)),
            "汉字",
            new byte[] { 0, 1, 255 },
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            new DateTime(638712864000000000L, DateTimeKind.Utc),
            new DateTimeOffset(
                638712864001234567L,
                TimeSpan.FromMinutes(330)),
            TimeSpan.FromTicks(123456789),
        };
        var second = new object?[columns.Length];

        ComputeBatch(columns, first, second).ShouldBe(
            "B7A06BFF17638BF50BC22DFB7D6D95829DF312FF048E85ACCD472592B3E9D755");
    }

    [Fact]
    public void Zero_Row_And_Order_Semantics_Must_Be_Deterministic()
    {
        ComputeBatch(new[] { "Id" }).ShouldBe(
            "16B099A7C8B3295AE57828AF677570D851C6EE6BE67830E71092299AE99B1739");

        var first = new object?[] { 1, "é" };
        var second = new object?[] { 2, "e\u0301" };
        var forward = ComputeBatch(new[] { "Id", "Text" }, first, second);
        var reversedRows = ComputeBatch(
            new[] { "Id", "Text" },
            second,
            first);
        var reversedColumns = ComputeBatch(
            new[] { "Text", "Id" },
            new object?[] { "é", 1 },
            new object?[] { "e\u0301", 2 });

        forward.ShouldNotBe(reversedRows);
        forward.ShouldNotBe(reversedColumns);
        "é".ShouldNotBe("e\u0301",
            "协议故意不做 Unicode normalization；不同源值必须保持可区分");
    }

    private static string ComputeBatch(
        IReadOnlyList<string> columns,
        params IReadOnlyList<object?>[] rows)
    {
        using var digest = new EtlReceiptHashes.BatchDigest(columns);
        foreach (var row in rows)
        {
            digest.AddRow(row);
        }

        return digest.Complete(rows.Length);
    }

    private static string ComputeExecutionContract(
        string coordinationIdentityHash) =>
        EtlReceiptHashes.ComputeExecutionContract(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "0123456789abcdef0123456789abcdef",
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            "fedcba9876543210fedcba9876543210",
            "sqlserver",
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            coordinationIdentityHash,
            new string('A', 64),
            new string('B', 64),
            "dbo.Target",
            EtlWriteMode.Append,
            new string('C', 64));

    [Fact]
    public void Execution_Contract_Must_Preserve_Legacy_Hash_When_Unmapped_And_Change_When_Mapped()
    {
        var unmappedNull = EtlReceiptHashes.ComputeExecutionContract(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "0123456789abcdef0123456789abcdef",
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            "fedcba9876543210fedcba9876543210",
            "postgresql",
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            new string('1', 64),
            new string('A', 64),
            new string('B', 64),
            "public.target",
            EtlWriteMode.Append,
            new string('C', 64));

        var unmappedEmpty = EtlReceiptHashes.ComputeExecutionContract(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "0123456789abcdef0123456789abcdef",
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            "fedcba9876543210fedcba9876543210",
            "postgresql",
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            new string('1', 64),
            new string('A', 64),
            new string('B', 64),
            "public.target",
            EtlWriteMode.Append,
            new string('C', 64),
            columnMappings: []);

        var mapped = EtlReceiptHashes.ComputeExecutionContract(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "0123456789abcdef0123456789abcdef",
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            "fedcba9876543210fedcba9876543210",
            "postgresql",
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            new string('1', 64),
            new string('A', 64),
            new string('B', 64),
            "public.target",
            EtlWriteMode.Append,
            new string('C', 64),
            columnMappings: [new EtlColumnMapping("src_id", "tgt_id")]);

        unmappedEmpty.ShouldBe(unmappedNull);
        mapped.ShouldNotBe(unmappedNull);
    }
}
