using Dapper;
using Turing_Backend.Models;

namespace Turing_Backend.Database;

public class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext ctx, DbConnectionFactory dbFactory)
    {
        // Не требуем авторизации для логина и SignalR-хаба (он сам проверит токен)
        if (ctx.Request.Path.StartsWithSegments("/api/Auth/login")
            || ctx.Request.Path.StartsWithSegments("/hubs"))
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Session-Id", out var sessionId))
        {
            await WriteSessionError(ctx, "NoSession",
                "Вы не авторизованы. Пожалуйста, войдите в систему.");
            return;
        }

        using var db = dbFactory.Create();
        var token = sessionId.ToString();

        var sessionData = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT u.Id, u.Login, u.FullName, u.Role, u.GroupId, s.LastActivityAt
              FROM Sessions s
              JOIN Users u ON s.UserId = u.Id
              WHERE s.Token = @Token",
            new { Token = token });

        if (sessionData == null)
        {
            var termination = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT Reason, Message FROM SessionTerminationReasons
                  WHERE Token = @Token
                  ORDER BY CreatedAt DESC LIMIT 1",
                new { Token = token });

            if (termination != null)
            {
                string reason = (string)termination.reason;
                string message = (string)termination.message;
                await db.ExecuteAsync(
                    "DELETE FROM SessionTerminationReasons WHERE Token = @Token",
                    new { Token = token });
                await WriteSessionError(ctx, reason, message);
                return;
            }

            // Сессия отсутствует в БД — возможно, она была явно удалена клиентом
            // через logout (нормальное завершение работы) или вытеснена новым входом
            // того же пользователя с другого устройства.
            await WriteSessionError(ctx, "NoSession",
                "Ваша сессия не найдена на сервере. Пожалуйста, войдите заново.");
            return;
        }

        // Автоматическое завершение сессии по бездействию пользователя ОТКЛЮЧЕНО.
        // Сессия живёт до тех пор, пока пользователь явно не выйдет, либо пока её не
        // завершит администратор (смена логина/пароля, удаление учётной записи), либо
        // пока тот же пользователь не войдёт с другого устройства, имея активный
        // SignalR-канал.
        //
        // Поле LastActivityAt по-прежнему обновляется на каждый успешный REST-запрос —
        // оно используется только для информирования администратора о последней активности
        // пользователя в окне «Состояние онлайн». На жизнь сессии оно не влияет.
        await db.ExecuteAsync(
            "UPDATE Sessions SET LastActivityAt = @Now WHERE Token = @Token",
            new { Now = DateTime.Now, Token = token });

        ctx.Items["UserId"] = (int)sessionData.id;
        ctx.Items["Role"] = (string)sessionData.role;

        await _next(ctx);
    }

    private static async Task WriteSessionError(HttpContext ctx, string reason, string message)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var json = System.Text.Json.JsonSerializer.Serialize(new { Reason = reason, Message = message });
        await ctx.Response.WriteAsync(json);
    }
}
