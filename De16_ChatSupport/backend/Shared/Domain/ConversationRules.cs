namespace ChatSupport.Shared.Domain;

/// <summary>Quy tắc nghiệp vụ thuần (không phụ thuộc hạ tầng) để dễ kiểm thử.</summary>
public static class ConversationRules
{
    public const int MaxMessageLength = 2000;

    /// <summary>Waiting→Active (tiếp nhận), Active→Closed (đóng), Waiting→Closed (khách huỷ).</summary>
    public static bool CanTransition(ConversationStatus from, ConversationStatus to) => (from, to) switch
    {
        (ConversationStatus.Waiting, ConversationStatus.Active) => true,
        (ConversationStatus.Active, ConversationStatus.Closed) => true,
        (ConversationStatus.Waiting, ConversationStatus.Closed) => true,
        _ => false
    };

    /// <summary>Trả về thông báo lỗi nếu nội dung không hợp lệ, ngược lại null.</summary>
    public static string? ValidateMessageContent(string? content, out string normalized)
    {
        normalized = (content ?? string.Empty).Trim();
        if (normalized.Length == 0) return "Nội dung tin nhắn không được để trống.";
        if (normalized.Length > MaxMessageLength) return $"Tin nhắn tối đa {MaxMessageLength} ký tự.";
        return null;
    }

    /// <summary>Chuẩn hoá về UTC để Npgsql (timestamptz) không từ chối.</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
