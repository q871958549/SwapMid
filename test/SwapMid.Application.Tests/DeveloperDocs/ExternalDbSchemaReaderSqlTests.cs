using Shouldly;
using Xunit;

namespace SwapMid.DeveloperDocs;

public class ExternalDbSchemaReaderSqlTests
{
    [Fact]
    public void SqlServerTablesSql_Should_Filter_User_Tables_With_Parameterized_Top()
    {
        var sql = ExternalDbSchemaReader.SqlServerTablesSql;

        sql.ShouldContain("TOP (@limitPlusOne)");
        sql.ShouldContain("t.is_ms_shipped = 0");
        sql.ShouldContain("MS_Description");
    }

    [Fact]
    public void PostgreSqlTablesSql_Should_Query_Public_Ordinary_Tables_With_Parameterized_Limit()
    {
        var sql = ExternalDbSchemaReader.PostgreSqlTablesSql;

        sql.ShouldContain("c.relkind = 'r'");
        sql.ShouldContain("n.nspname = 'public'");
        sql.ShouldContain("LIMIT @limitPlusOne");
    }

    [Fact]
    public void MySqlTablesSql_Should_Read_InformationSchema_Tables()
    {
        var sql = ExternalDbSchemaReader.MySqlTablesSql;

        sql.ShouldContain("information_schema.TABLES");
        sql.ShouldContain("DATABASE()");
    }
}
