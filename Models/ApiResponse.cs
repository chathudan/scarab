namespace Scarab.Models;

public class ApiResponse
{
    public string Status { get; set; } = "success";
    public string? Message { get; set; }
    public object? Data { get; set; }

    public static ApiResponse Success(object? data) => new() { Status = "success", Data = data };
    public static ApiResponse Error(string message) => new() { Status = "error", Message = message };
}
