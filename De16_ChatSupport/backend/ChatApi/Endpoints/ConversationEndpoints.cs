using System.Net.Mail;
using System.Security.Claims;
using ChatSupport.ChatApi.Models;
using ChatSupport.ChatApi.Services;
using ChatSupport.Shared.Domain;

namespace ChatSupport.ChatApi.Endpoints;

public static class ConversationEndpoints
{
    public static void MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/conversations").WithTags("Conversations");

        group.MapPost("", CreateAsync)
            .AllowAnonymous()
            .WithSummary("Khách hàng tạo phiên hỗ trợ mới (trạng thái Waiting)")
            .Produces<CreateConversationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("", ListAsync)
            .RequireAuthorization("Agent")
            .WithSummary("Danh sách phiên (lọc theo status, tìm kiếm, phân trang) - chỉ nhân viên")
            .Produces<PagedResult<ConversationDto>>()
            .ProducesValidationProblem();

        group.MapGet("/stats", StatsAsync)
            .RequireAuthorization("Agent")
            .WithSummary("Số phiên theo trạng thái - chỉ nhân viên")
            .Produces<ConversationStats>();

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization()
            .WithSummary("Chi tiết một phiên")
            .Produces<ConversationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/messages", MessagesAsync)
            .RequireAuthorization()
            .WithSummary("Lịch sử tin nhắn theo thứ tự (phân trang theo afterSequence)")
            .Produces<MessagePage>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/accept", AcceptAsync)
            .RequireAuthorization("Agent")
            .WithSummary("Nhân viên tiếp nhận phiên (Waiting → Active)")
            .Produces<ConversationDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/close", CloseAsync)
            .RequireAuthorization()
            .WithSummary("Đóng phiên (→ Closed) và phát sự kiện kết thúc")
            .Produces<ConversationDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> CreateAsync(CreateConversationRequest? request, ChatService chat, TokenService tokens, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request?.CustomerName?.Trim() ?? string.Empty;
        var email = request?.CustomerEmail?.Trim();
        var topic = request?.Topic?.Trim() ?? string.Empty;

        if (name.Length < 2 || name.Length > 100)
            errors["customerName"] = ["Tên phải từ 2 đến 100 ký tự."];
        if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
            errors["customerEmail"] = ["Email không hợp lệ."];
        if (topic.Length < 3 || topic.Length > 200)
            errors["topic"] = ["Nội dung cần hỗ trợ phải từ 3 đến 200 ký tự."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var (dto, customer) = await chat.CreateConversationAsync(name, email, topic, ct);
        var (token, expires) = tokens.CreateToken(customer, dto.Id);
        return Results.Created($"/conversations/{dto.Id}", new CreateConversationResponse(dto, token, expires));
    }

    private static async Task<IResult> ListAsync(
        string? status, string? search, bool? mine, int? page, int? pageSize,
        ClaimsPrincipal user, ChatService chat, CancellationToken ct)
    {
        ConversationStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ConversationStatus>(status, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Trạng thái phải là Waiting, Active hoặc Closed."]
                });
            parsed = s;
        }

        var result = await chat.ListConversationsAsync(Actor.From(user), parsed, search, mine ?? false, page ?? 1, pageSize ?? 20, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> StatsAsync(ChatService chat, CancellationToken ct) =>
        Results.Ok(await chat.GetStatsAsync(ct));

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ChatService chat, CancellationToken ct) =>
        Results.Ok(await chat.GetConversationAsync(Actor.From(user), id, ct));

    private static async Task<IResult> MessagesAsync(Guid id, long? afterSequence, int? limit, ClaimsPrincipal user, ChatService chat, CancellationToken ct)
    {
        if (afterSequence is < 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["afterSequence"] = ["afterSequence phải >= 0."] });
        if (limit is < 1 or > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = ["limit phải từ 1 đến 500."] });

        return Results.Ok(await chat.GetMessagesAsync(Actor.From(user), id, afterSequence ?? 0, limit ?? 100, ct));
    }

    private static async Task<IResult> AcceptAsync(Guid id, ClaimsPrincipal user, ChatService chat, CancellationToken ct) =>
        Results.Ok(await chat.AcceptAsync(Actor.From(user), id, ct));

    private static async Task<IResult> CloseAsync(Guid id, ClaimsPrincipal user, ChatService chat, CancellationToken ct) =>
        Results.Ok(await chat.CloseAsync(Actor.From(user), id, ct));

    private static bool IsValidEmail(string email) =>
        email.Length <= 200 && MailAddress.TryCreate(email, out var addr) && addr.Address == email;
}
