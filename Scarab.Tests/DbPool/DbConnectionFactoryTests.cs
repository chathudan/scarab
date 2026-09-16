using Scarab.DbPool;
using Scarab.Config;

namespace Scarab.Tests.DbPool;

public class DbConnectionFactoryTests
{
    [Theory]
    [InlineData("mssql", DbType.Mssql)]
    [InlineData("mysql", DbType.MySql)]
    [InlineData("postgres", DbType.PostgreSql)]
    [InlineData("postgresql", DbType.PostgreSql)]
    [InlineData("MSSQL", DbType.Mssql)]
    [InlineData("MySQL", DbType.MySql)]
    public void ParseDbType_ValidTypes_ReturnsCorrectEnum(string input, DbType expected)
    {
        Assert.Equal(expected, DbConnectionFactory.ParseDbType(input));
    }

    [Fact]
    public void ParseDbType_InvalidType_Throws()
    {
        Assert.Throws<ArgumentException>(() => DbConnectionFactory.ParseDbType("oracle"));
    }

    [Fact]
    public void Constructor_SetsNameAndType()
    {
        var config = new DbPoolConfig { Type = "postgres", Dsn = "Host=localhost;Database=test;" };
        var factory = new DbConnectionFactory("mydb", config);

        Assert.Equal("mydb", factory.Name);
        Assert.Equal(DbType.PostgreSql, factory.Type);
    }
}
