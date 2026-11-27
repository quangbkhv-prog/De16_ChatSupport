using ChatSupport.ChatApi.Options;
using ChatSupport.Shared.Data;
using ChatSupport.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatSupport.ChatApi.Services;

/// <summary>Áp dụng EF Core migrations khi khởi động và nạp dữ liệu mẫu (idempotent).</summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < 30 && !ct.IsCancellationRequested)
            {
                logger.LogWarning("PostgreSQL chưa sẵn sàng (lần {Attempt}/30): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        logger.LogInformation("Database schema đã được cập nhật bằng EF Core migrations");

        await SeedAgentsAsync(db, seed, logger, ct);
        if (seed.DemoData) await SeedDemoConversationsAsync(db, logger, ct);
    }

    private static async Task SeedAgentsAsync(ChatDbContext db, SeedOptions seed, ILogger logger, CancellationToken ct)
    {
        var agents = new[]
        {
            ("agent1", "Nguyễn Minh Anh"),
            ("agent2", "Trần Quốc Bảo")
        };

        foreach (var (username, displayName) in agents)
        {
            if (await db.Participants.AnyAsync(p => p.Username == username, ct)) continue;

            db.Participants.Add(new Participant
            {
                Id = Guid.NewGuid(),
                DisplayName = displayName,
                Role = ParticipantRole.Agent,
                Username = username,
                PasswordHash = PasswordHasher.Hash(seed.AgentPassword),
                CreatedAt = DateTime.UtcNow
            });
            logger.LogInformation("Đã tạo tài khoản nhân viên mẫu {Username}", username);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Hai phiên đã đóng kèm tin nhắn để demo màn hình Lịch sử ngay sau khi chạy.</summary>
    private static async Task SeedDemoConversationsAsync(ChatDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Conversations.AnyAsync(ct)) return;

        var agent1 = await db.Participants.FirstAsync(p => p.Username == "agent1", ct);
        var agent2 = await db.Participants.FirstAsync(p => p.Username == "agent2", ct);
        var baseTime = DateTime.UtcNow.AddDays(-2);

        var demos = new[]
        {
            (Customer: "Lê Thu Hà", Email: "thuha@example.com", Topic: "Không đăng nhập được vào tài khoản", Agent: agent1,
             Lines: new[]
             {
                 (false, "Chào bạn, tôi không đăng nhập được vào tài khoản từ sáng nay."),
                 (true, "Chào chị Hà, em là Minh Anh. Chị cho em biết email đăng ký để em kiểm tra nhé?"),
                 (false, "Email của tôi là thuha@example.com."),
                 (true, "Em đã đặt lại mật khẩu cho chị, chị kiểm tra hộp thư và đăng nhập lại giúp em nhé.")
             }),
            (Customer: "Phạm Văn Đức", Email: "ducpv@example.com", Topic: "Hỏi về hoá đơn tháng 10", Agent: agent2,
             Lines: new[]
             {
                 (false, "Cho mình hỏi hoá đơn tháng 10 vì sao cao hơn tháng trước?"),
                 (true, "Chào anh Đức, em là Quốc Bảo. Em đã kiểm tra: tháng 10 có thêm phí gia hạn dịch vụ."),
                 (false, "Mình hiểu rồi, cảm ơn bạn.")
             })
        };

        var offset = 0;
        foreach (var d in demos)
        {
            var created = baseTime.AddHours(offset++);
            var customer = new Participant
            {
                Id = Guid.NewGuid(), DisplayName = d.Customer, Email = d.Email,
                Role = ParticipantRole.Customer, CreatedAt = created
            };
            var conv = new Conversation
            {
                Id = Guid.NewGuid(), Topic = d.Topic, CustomerId = customer.Id, AgentId = d.Agent.Id,
                Status = ConversationStatus.Closed, CreatedAt = created,
                AcceptedAt = created.AddMinutes(2), ClosedAt = created.AddMinutes(2 + d.Lines.Length * 2),
                ClosedById = d.Agent.Id, LastSequence = d.Lines.Length, MessageCount = d.Lines.Length,
                LastMessageAt = created.AddMinutes(2 + d.Lines.Length * 2 - 1),
                HistorySyncedAt = created.AddMinutes(3 + d.Lines.Length * 2)
            };
            db.Participants.Add(customer);
            db.Conversations.Add(conv);

            var seq = 0;
            foreach (var (fromAgent, text) in d.Lines)
            {
                seq++;
                db.Messages.Add(new ChatMessage
                {
                    Id = Guid.NewGuid(), ConversationId = conv.Id,
                    SenderId = fromAgent ? d.Agent.Id : customer.Id,
                    Sequence = seq, Content = text,
                    SentAt = created.AddMinutes(2 + seq * 2 - 1),
                    StoredAt = created.AddMinutes(2 + seq * 2 - 1)
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Đã nạp {Count} phiên đã đóng làm dữ liệu mẫu", demos.Length);
    }
}
