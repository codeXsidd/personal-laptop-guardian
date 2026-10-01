using System.Text.Json;
using LaptopGuardian.Desktop.Services;
using Xunit;

namespace LaptopGuardian.Desktop.Tests.Services;

public class IpcResponseTests
{
    [Fact]
    public void Error_CreatesFailedResponse()
    {
        var response = IpcResponse.Error("test error");
        Assert.False(response.Success);
        Assert.Equal("test error", response.ErrorMessage);
        Assert.Null(response.Data);
    }

    [Fact]
    public void GetString_ReturnsValue()
    {
        var response = new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.Deserialize<JsonElement>("\"hello\""),
            }
        };
        Assert.Equal("hello", response.GetString("name"));
    }

    [Fact]
    public void GetString_ReturnNullForMissing()
    {
        var response = new IpcResponse { Success = true, Data = new() };
        Assert.Null(response.GetString("missing"));
    }

    [Fact]
    public void GetBool_ReturnsTrue()
    {
        var response = new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, JsonElement>
            {
                ["flag"] = JsonSerializer.Deserialize<JsonElement>("true"),
            }
        };
        Assert.True(response.GetBool("flag"));
    }

    [Fact]
    public void GetBool_ReturnsFalseForMissing()
    {
        var response = new IpcResponse { Success = true, Data = new() };
        Assert.False(response.GetBool("missing"));
    }

    [Fact]
    public void GetInt_ReturnsValue()
    {
        var response = new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, JsonElement>
            {
                ["count"] = JsonSerializer.Deserialize<JsonElement>("42"),
            }
        };
        Assert.Equal(42, response.GetInt("count"));
    }

    [Fact]
    public void GetInt_ReturnsFallbackForMissing()
    {
        var response = new IpcResponse { Success = true, Data = new() };
        Assert.Equal(99, response.GetInt("missing", 99));
    }

    [Fact]
    public void GetDouble_ReturnsValue()
    {
        var response = new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, JsonElement>
            {
                ["pct"] = JsonSerializer.Deserialize<JsonElement>("55.5"),
            }
        };
        Assert.Equal(55.5, response.GetDouble("pct"));
    }

    [Fact]
    public void GetDouble_ReturnsNullForMissing()
    {
        var response = new IpcResponse { Success = true, Data = new() };
        Assert.Null(response.GetDouble("missing"));
    }
}
