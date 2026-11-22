using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChatSupport.Shared.Data;

/// <summary>Dùng cho công cụ `dotnet ef` (design-time). Không dùng khi chạy thật.</summary>
public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Port=5432;Database=chatsupport;Username=chat_user;Password=ChangeMe_Postgres_123";
        var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(cs).Options;
        return new ChatDbContext(options);
    }
}
