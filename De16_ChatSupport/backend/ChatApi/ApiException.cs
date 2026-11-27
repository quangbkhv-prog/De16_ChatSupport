namespace ChatSupport.ChatApi;

/// <summary>Lỗi nghiệp vụ có mã HTTP rõ ràng; được middleware chuyển thành ProblemDetails.</summary>
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ApiException NotFound(string message = "Không tìm thấy phiên hỗ trợ.") => new(404, "not_found", message);
    public static ApiException Forbidden(string message = "Bạn không có quyền thực hiện thao tác này.") => new(403, "forbidden", message);
    public static ApiException Conflict(string code, string message) => new(409, code, message);
    public static ApiException BadRequest(string code, string message) => new(400, code, message);
}
