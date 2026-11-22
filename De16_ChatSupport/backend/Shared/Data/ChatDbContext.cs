using ChatSupport.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace ChatSupport.Shared.Data;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Participant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Username).HasMaxLength(50);
            e.Property(x => x.PasswordHash).HasMaxLength(300);
            e.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.AgentId);
            e.HasIndex(x => x.CustomerId);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasMaxLength(2000).IsRequired();
            e.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.ConversationId, x.SentAt });
            e.HasIndex(x => x.SenderId);
        });

        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).HasColumnType("text").IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.Id).HasDatabaseName("IX_OutboxEvents_Pending").HasFilter("\"PublishedAt\" IS NULL");
            e.HasIndex(x => new { x.ConversationId, x.EventType });
        });
    }
}
