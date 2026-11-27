namespace ChatSupport.ChatApi.Models;

public sealed record CreateConversationRequest(string? CustomerName, string? CustomerEmail, string? Topic);

public sealed record CreateConversationResponse(ConversationDto Conversation, string AccessToken, DateTime ExpiresAt);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record AgentDto(Guid Id, string DisplayName, string Username);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, AgentDto Agent);

public sealed record ConversationDto(
    Guid Id,
    string Topic,
    string Status,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    Guid? AgentId,
    string? AgentName,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    DateTime? ClosedAt,
    int MessageCount,
    DateTime? LastMessageAt,
    long LastSequence);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string SenderRole,
    long Sequence,
    string Content,
    DateTime SentAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Phân trang theo con trỏ Sequence: gọi tiếp với afterSequence = NextAfterSequence khi HasMore.</summary>
public sealed record MessagePage(IReadOnlyList<MessageDto> Items, bool HasMore, long NextAfterSequence);

public sealed record ConversationStats(int Waiting, int Active, int Closed);

public sealed record SendResult(MessageDto Message, bool Duplicate);
