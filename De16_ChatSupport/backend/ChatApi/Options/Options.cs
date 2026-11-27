namespace ChatSupport.ChatApi.Options;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "chat-support";
    public string Audience { get; set; } = "chat-support-clients";

    /// <summary>Khoá ký JWT (tối thiểu 32 ký tự). Đọc từ biến môi trường Jwt__Secret.</summary>
    public string Secret { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 720;
}

public sealed class SeedOptions
{
    /// <summary>Mật khẩu của các tài khoản nhân viên mẫu (agent1, agent2). Chỉ dùng cho demo.</summary>
    public string AgentPassword { get; set; } = "Agent@123";
    public bool DemoData { get; set; } = true;
}
