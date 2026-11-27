using System.Security.Claims;

namespace ChatSupport.ChatApi.Services;

/// <summary>Danh tính người gọi, rút ra từ JWT (sub, name, role, cid).</summary>
public sealed record Actor(Guid Id, string Name, string Role, Guid? ConversationId)
{
    public const string AgentRole = "agent";
    public const string CustomerRole = "customer";

    public bool IsAgent => Role == AgentRole;

    public static Actor From(ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirstValue("sub"), out var id))
            throw new ApiException(401, "unauthorized", "Thiếu hoặc sai thông tin đăng nhập.");

        var role = user.FindFirstValue("role") ?? string.Empty;
        if (role != AgentRole && role != CustomerRole)
            throw new ApiException(401, "unauthorized", "Vai trò không hợp lệ.");

        Guid? cid = Guid.TryParse(user.FindFirstValue("cid"), out var c) ? c : null;
        return new Actor(id, user.FindFirstValue("name") ?? "Người dùng", role, cid);
    }
}
