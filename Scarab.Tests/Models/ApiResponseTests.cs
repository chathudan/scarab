using Scarab.Models;

namespace Scarab.Tests.Models;

public class ApiResponseTests
{
    [Fact]
    public void Success_SetsStatusAndData()
    {
        var result = ApiResponse.Success("test-data");

        Assert.Equal("success", result.Status);
        Assert.Equal("test-data", result.Data);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Error_SetsStatusAndMessage()
    {
        var result = ApiResponse.Error("something broke");

        Assert.Equal("error", result.Status);
        Assert.Equal("something broke", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Success_WithNullData_ReturnsSuccess()
    {
        var result = ApiResponse.Success(null);

        Assert.Equal("success", result.Status);
        Assert.Null(result.Data);
    }
}
