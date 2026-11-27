using ChatSupport.ChatApi.Hubs;
using ChatSupport.ChatApi.Models;
using ChatSupport.Shared.Data;
using ChatSupport.Shared.Domain;
using ChatSupport.Shared.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatSupport.ChatApi.Services;

/// <summary>
/// Chat Service: toàn bộ nghiệp vụ phiên chat. Mọi chuyển trạng thái đều là câu lệnh UPDATE có điều kiện
/// (compare-and-set) nên an toàn khi nhiều người thao tác đồng thời.
/// </summary>
public sealed class ChatService(
    ChatDbContext db,
    IHubContext<ChatHub> hub,
    OutboxSignal signal,
    ILogger<ChatService> log)
{
    // ───────────────────────── Tạo phiên ─────────────────────────

    public async Task<(ConversationDto Conversation, Participant Customer)> CreateConversationAsync(
        string name, string? email, string topic, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var customer = new Participant
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Role = ParticipantRole.Customer,
            CreatedAt = now
        };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            CustomerId = customer.Id,
            Customer = customer,
            Status = ConversationStatus.Waiting,
            CreatedAt = now
        };

        db.Participants.Add(customer);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(conversation);
        log.LogInformation("Phiên {ConversationId} được tạo bởi khách {Customer}", dto.Id, dto.CustomerName);
        await BroadcastAsync(GroupNames.Agents, HubEvents.ConversationCreated, dto);
        return (dto, customer);
    }

    // ───────────────────────── Truy vấn ─────────────────────────

    public async Task<ConversationDto> GetConversationAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var conv = await LoadAsync(id, ct);
        EnsureCanRead(actor, conv);
        return ToDto(conv);
    }

    public async Task<PagedResult<ConversationDto>> ListConversationsAsync(
        Actor actor, ConversationStatus? status, string? search, bool mine, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Conversation> query = db.Conversations.AsNoTracking();
        if (status is { } s) query = query.Where(c => c.Status == s);
        if (mine) query = query.Where(c => c.AgentId == actor.Id);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Topic.ToLower().Contains(term) ||
                c.Customer.DisplayName.ToLower().Contains(term) ||
                (c.Customer.Email != null && c.Customer.Email.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var ordered = status == ConversationStatus.Waiting
            ? query.OrderBy(c => c.CreatedAt)
            : query.OrderByDescending(c => c.CreatedAt);

        var rows = await ordered
            .Include(c => c.Customer)
            .Include(c => c.Agent)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ConversationDto>(rows.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ConversationStats> GetStatsAsync(CancellationToken ct)
    {
        var counts = await db.Conversations.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Of(ConversationStatus s) => counts.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
        return new ConversationStats(Of(ConversationStatus.Waiting), Of(ConversationStatus.Active), Of(ConversationStatus.Closed));
    }

    /// <summary>
    /// Lịch sử tin nhắn theo thứ tự Sequence. Gộp tin đã lưu (bảng Messages) với tin còn nằm trong Outbox
    /// mà Worker chưa kịp lưu, nên người dùng không bao giờ thấy "hụt" tin khi Worker/RabbitMQ đang chậm.
    /// </summary>
    public async Task<MessagePage> GetMessagesAsync(Actor actor, Guid conversationId, long afterSequence, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var conv = await LoadAsync(conversationId, ct);
        EnsureCanRead(actor, conv);

        var stored = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Sequence > afterSequence)
            .OrderBy(m => m.Sequence)
            .Take(limit + 1)
            .Select(m => new
            {
                m.Id, m.ConversationId, m.SenderId, SenderName = m.Sender.DisplayName,
                SenderRole = m.Sender.Role, m.Sequence, m.Content, m.SentAt
            })
            .ToListAsync(ct);

        var items = stored
            .Select(m => new MessageDto(m.Id, m.ConversationId, m.SenderId, m.SenderName,
                m.SenderRole == ParticipantRole.Agent ? Actor.AgentRole : Actor.CustomerRole,
                m.Sequence, m.Content, m.SentAt))
            .ToList();

        var pendingPayloads = await db.OutboxEvents.AsNoTracking()
            .Where(o => o.ConversationId == conversationId
                        && o.EventType == MessagingConstants.MessageSentKey
                        && !db.Messages.Any(m => m.Id == o.EventId))
            .OrderBy(o => o.Id)
            .Select(o => o.Payload)
            .ToListAsync(ct);

        foreach (var payload in pendingPayloads)
        {
            var evt = EventSerializer.Deserialize<MessageSentEvent>(payload);
            if (evt is null || evt.Sequence <= afterSequence) continue;
            items.Add(ToDto(evt));
        }

        var merged = items.OrderBy(m => m.Sequence).ToList();
        var hasMore = merged.Count > limit;
        if (hasMore) merged = merged.Take(limit).ToList();
        var next = merged.Count > 0 ? merged[^1].Sequence : afterSequence;
        return new MessagePage(merged, hasMore, next);
    }

    // ───────────────────────── Nhân viên tiếp nhận ─────────────────────────

    public async Task<ConversationDto> AcceptAsync(Actor actor, Guid conversationId, CancellationToken ct)
    {
        if (!actor.IsAgent) throw ApiException.Forbidden("Chỉ nhân viên mới được tiếp nhận phiên.");

        var conv = await LoadAsync(conversationId, ct);
        if (conv.Status == ConversationStatus.Closed)
            throw ApiException.Conflict("conversation_closed", "Phiên đã đóng, không thể tiếp nhận.");

        var now = DateTime.UtcNow;
        // Compare-and-set: chỉ một nhân viên thắng khi nhiều người bấm cùng lúc.
        var rows = await db.Conversations
            .Where(c => c.Id == conversationId && c.Status == ConversationStatus.Waiting)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ConversationStatus.Active)
                .SetProperty(c => c.AgentId, (Guid?)actor.Id)
                .SetProperty(c => c.AcceptedAt, (DateTime?)now), ct);

        if (rows == 0)
        {
            var current = await LoadAsync(conversationId, ct);
            if (current.Status == ConversationStatus.Closed)
                throw ApiException.Conflict("conversation_closed", "Phiên đã đóng, không thể tiếp nhận.");
            if (current.AgentId == actor.Id) return ToDto(current); // gọi lặp lại: idempotent
            throw ApiException.Conflict("already_accepted", "Phiên đã có nhân viên khác tiếp nhận.");
        }

        var dto = ToDto(await LoadAsync(conversationId, ct));
        log.LogInformation("Phiên {ConversationId} được nhân viên {Agent} tiếp nhận", dto.Id, actor.Name);
        await BroadcastAsync(GroupNames.Conversation(dto.Id), HubEvents.ConversationUpdated, dto);
        await BroadcastAsync(GroupNames.Agents, HubEvents.ConversationUpdated, dto);
        return dto;
    }

    // ───────────────────────── Đóng phiên ─────────────────────────

    public async Task<ConversationDto> CloseAsync(Actor actor, Guid conversationId, CancellationToken ct)
    {
        var conv = await LoadAsync(conversationId, ct);
        EnsureCanClose(actor, conv);

        if (!ConversationRules.CanTransition(conv.Status, ConversationStatus.Closed))
            throw ApiException.Conflict("conversation_closed", "Phiên đã được đóng trước đó.");

        var now = DateTime.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.Conversations
                .Where(c => c.Id == conversationId && c.Status != ConversationStatus.Closed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ConversationStatus.Closed)
                    .SetProperty(c => c.ClosedAt, (DateTime?)now)
                    .SetProperty(c => c.ClosedById, (Guid?)actor.Id), ct);
            if (rows == 0)
                throw ApiException.Conflict("conversation_closed", "Phiên đã được đóng trước đó.");

            // Hàng đã bị khoá bởi UPDATE nên không còn tin nhắn nào được cấp Sequence mới sau thời điểm này.
            var lastSequence = await db.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastSequence)
                .SingleAsync(ct);

            var evt = new ConversationClosedEvent(Guid.NewGuid(), conversationId, actor.Id, actor.Role, now, lastSequence);
            db.OutboxEvents.Add(new OutboxEvent
            {
                EventId = evt.EventId,
                EventType = MessagingConstants.ConversationClosedKey,
                ConversationId = conversationId,
                Payload = EventSerializer.Serialize(evt),
                CreatedAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        signal.Notify();
        var dto = ToDto(await LoadAsync(conversationId, ct));
        log.LogInformation("Phiên {ConversationId} đã đóng bởi {Actor} ({Role})", dto.Id, actor.Name, actor.Role);
        await BroadcastAsync(GroupNames.Conversation(dto.Id), HubEvents.ConversationClosed, dto);
        await BroadcastAsync(GroupNames.Agents, HubEvents.ConversationUpdated, dto);
        return dto;
    }

    // ───────────────────────── Realtime: Join / Send ─────────────────────────

    /// <summary>Kiểm tra quyền trước khi thêm kết nối vào SignalR Group của phiên.</summary>
    public async Task<ConversationDto> AuthorizeJoinAsync(Actor actor, Guid conversationId, CancellationToken ct)
    {
        var conv = await LoadAsync(conversationId, ct);
        EnsureIsParticipant(actor, conv);
        return ToDto(conv);
    }

    public async Task<SendResult> SendMessageAsync(
        Actor actor, Guid conversationId, string? content, Guid? clientMessageId, CancellationToken ct)
    {
        var error = ConversationRules.ValidateMessageContent(content, out var text);
        if (error is not null) throw ApiException.BadRequest("invalid_message", error);

        var conv = await LoadAsync(conversationId, ct);
        EnsureIsParticipant(actor, conv);
        if (conv.Status == ConversationStatus.Closed)
            throw ApiException.Conflict("conversation_closed", "Phiên đã đóng, không thể gửi thêm tin nhắn.");
        if (actor.IsAgent && conv.Status != ConversationStatus.Active)
            throw ApiException.Conflict("conversation_not_active", "Chỉ gửi tin nhắn khi phiên đang Active.");

        var messageId = clientMessageId is { } cm && cm != Guid.Empty ? cm : Guid.NewGuid();

        // Idempotent: client gửi lại cùng MessageId (ví dụ sau khi mất kết nối) không tạo tin mới.
        var existing = await db.OutboxEvents.AsNoTracking()
            .Where(o => o.EventId == messageId)
            .Select(o => new { o.ConversationId, o.Payload })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            var previous = EventSerializer.Deserialize<MessageSentEvent>(existing.Payload);
            if (previous is null || existing.ConversationId != conversationId || previous.SenderId != actor.Id)
                throw ApiException.Conflict("duplicate_message_id", "MessageId đã được dùng cho tin nhắn khác.");
            return new SendResult(ToDto(previous), Duplicate: true);
        }

        var now = DateTime.UtcNow;
        MessageSentEvent evt;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Cấp Sequence nguyên tử; UPDATE cũng chặn gửi vào phiên vừa bị đóng (race với Close).
            var rows = await db.Conversations
                .Where(c => c.Id == conversationId && c.Status != ConversationStatus.Closed)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSequence, c => c.LastSequence + 1), ct);
            if (rows == 0)
                throw ApiException.Conflict("conversation_closed", "Phiên đã đóng, không thể gửi thêm tin nhắn.");

            var sequence = await db.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => c.LastSequence)
                .SingleAsync(ct);

            evt = new MessageSentEvent(messageId, conversationId, actor.Id, actor.Name, actor.Role, sequence, text, now);
            db.OutboxEvents.Add(new OutboxEvent
            {
                EventId = messageId,
                EventType = MessagingConstants.MessageSentKey,
                ConversationId = conversationId,
                Payload = EventSerializer.Serialize(evt),
                CreatedAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Không ghi được tin nhắn {MessageId} vào Outbox", messageId);
            throw ApiException.Conflict("message_not_saved", "Không thể lưu tin nhắn, vui lòng thử lại.");
        }

        signal.Notify();
        var dto = ToDto(evt);
        log.LogInformation("Tin nhắn {MessageId} (#{Sequence}) trong phiên {ConversationId} từ {Role}",
            dto.Id, dto.Sequence, dto.ConversationId, dto.SenderRole);
        await BroadcastAsync(GroupNames.Conversation(conversationId), HubEvents.ReceiveMessage, dto);
        return new SendResult(dto, Duplicate: false);
    }

    // ───────────────────────── Nội bộ ─────────────────────────

    private async Task<Conversation> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Conversations.AsNoTracking()
            .Include(c => c.Customer)
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw ApiException.NotFound();

    private static void EnsureCanRead(Actor actor, Conversation conv)
    {
        if (actor.IsAgent) return;
        if (actor.ConversationId != conv.Id || actor.Id != conv.CustomerId) throw ApiException.Forbidden();
    }

    /// <summary>Người tham gia hợp lệ: khách sở hữu phiên, hoặc nhân viên đang phụ trách phiên.</summary>
    private static void EnsureIsParticipant(Actor actor, Conversation conv)
    {
        if (actor.IsAgent)
        {
            if (conv.AgentId != actor.Id)
                throw ApiException.Forbidden("Bạn không phải nhân viên phụ trách phiên này.");
            return;
        }
        if (actor.ConversationId != conv.Id || actor.Id != conv.CustomerId) throw ApiException.Forbidden();
    }

    private static void EnsureCanClose(Actor actor, Conversation conv)
    {
        if (actor.IsAgent)
        {
            // Nhân viên phụ trách được đóng; phiên còn Waiting thì nhân viên nào cũng được đóng (loại bỏ phiên rác).
            if (conv.AgentId != actor.Id && conv.Status != ConversationStatus.Waiting)
                throw ApiException.Forbidden("Chỉ nhân viên phụ trách mới được đóng phiên này.");
            return;
        }
        if (actor.ConversationId != conv.Id || actor.Id != conv.CustomerId) throw ApiException.Forbidden();
    }

    private async Task BroadcastAsync<T>(string group, string method, T payload)
    {
        try
        {
            await hub.Clients.Group(group).SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            // Lỗi phát realtime không được làm hỏng thao tác nghiệp vụ đã commit.
            log.LogWarning(ex, "Không phát được sự kiện {Method} tới nhóm {Group}", method, group);
        }
    }

    public static ConversationDto ToDto(Conversation c) => new(
        c.Id, c.Topic, c.Status.ToString(), c.CustomerId, c.Customer.DisplayName, c.Customer.Email,
        c.AgentId, c.Agent?.DisplayName, c.CreatedAt, c.AcceptedAt, c.ClosedAt,
        c.MessageCount, c.LastMessageAt, c.LastSequence);

    public static MessageDto ToDto(MessageSentEvent e) => new(
        e.MessageId, e.ConversationId, e.SenderId, e.SenderName, e.SenderRole, e.Sequence, e.Content, e.SentAt);
}
