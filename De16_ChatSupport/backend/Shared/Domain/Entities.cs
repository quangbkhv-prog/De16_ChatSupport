namespace ChatSupport.Shared.Domain;

public enum ParticipantRole
{
    Customer = 0,
    Agent = 1
}

/// <summary>Ba trạng thái duy nhất của một phiên hỗ trợ.</summary>
public enum ConversationStatus
{
    Waiting = 0,
    Active = 1,
    Closed = 2
}

/// <summary>Người tham gia: khách hàng (tạo theo từng phiên) hoặc nhân viên tư vấn (có tài khoản).</summary>
public class Participant
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public ParticipantRole Role { get; set; }
    public string? Email { get; set; }

    /// <summary>Chỉ dùng cho nhân viên.</summary>
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Participant Customer { get; set; } = null!;

    public Guid? AgentId { get; set; }
    public Participant? Agent { get; set; }

    public ConversationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }

    /// <summary>Số thứ tự tin nhắn cuối cùng đã được cấp (Chat Service tăng nguyên tử).</summary>
    public long LastSequence { get; set; }

    /// <summary>Số tin nhắn đã được History Worker lưu (do Worker cập nhật).</summary>
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }

    /// <summary>Thời điểm Worker xác nhận đã đồng bộ đủ lịch sử sau khi đóng phiên.</summary>
    public DateTime? HistorySyncedAt { get; set; }
}

/// <summary>Tin nhắn đã được History Worker lưu bền vững.</summary>
public class ChatMessage
{
    /// <summary>MessageId do client/Chat Service sinh; khoá chính giúp chống lưu trùng.</summary>
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid SenderId { get; set; }
    public Participant Sender { get; set; } = null!;
    public long Sequence { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime StoredAt { get; set; }
}

/// <summary>Bảng Outbox: sự kiện chờ phát vào RabbitMQ, ghi cùng giao dịch với thay đổi nghiệp vụ.</summary>
public class OutboxEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; }

    /// <summary>Đồng thời là routing key (message.sent, conversation.closed).</summary>
    public string EventType { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
