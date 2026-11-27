using System.Security.Claims;
using ChatSupport.ChatApi.Models;
using ChatSupport.ChatApi.Services;
using ChatSupport.Shared.Data;
using ChatSupport.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace ChatSupport.ChatApi.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithSummary("Nhân viên đăng nhập, nhận JWT")
            .Produces<LoginResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization("Agent")
            .WithSummary("Thông tin nhân viên đang đăng nhập")
            .Produces<AgentDto>();
    }

    private static async Task<IResult> LoginAsync(LoginRequest? request, ChatDbContext db, TokenService tokens, ILogger<LoginRequest> log, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var username = request?.Username?.Trim() ?? string.Empty;
        var password = request?.Password ?? string.Empty;
        if (username.Length == 0) errors["username"] = ["Vui lòng nhập tên đăng nhập."];
        if (password.Length == 0) errors["password"] = ["Vui lòng nhập mật khẩu."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var agent = await db.Participants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Username == username && p.Role == ParticipantRole.Agent, ct);

        if (agent is null || !PasswordHasher.Verify(password, agent.PasswordHash))
        {
            log.LogWarning("Đăng nhập thất bại cho tài khoản {Username}", username);
            return Results.Problem(title: "Sai tên đăng nhập hoặc mật khẩu.", statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });
        }

        var (token, expires) = tokens.CreateToken(agent);
        return Results.Ok(new LoginResponse(token, expires, new AgentDto(agent.Id, agent.DisplayName, agent.Username!)));
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal user, ChatDbContext db, CancellationToken ct)
    {
        var actor = Actor.From(user);
        var agent = await db.Participants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == actor.Id, ct);
        return agent is null
            ? Results.Unauthorized()
            : Results.Ok(new AgentDto(agent.Id, agent.DisplayName, agent.Username ?? string.Empty));
    }
}
