using Microsoft.AspNetCore.SignalR;
using Dapper;
using System.Collections.Concurrent;
using Turing_Backend.Database;

namespace Turing_Backend.Hubs;

/// <summary>
/// Центральный хаб SignalR. Используется только для доставки push-уведомлений клиенту
/// (завершение сессии администратором, изменение профиля, изменение задания и т.п.).
///
/// Жизненный цикл сессии НЕ зависит от состояния SignalR-подключения.
/// Если у клиента нет активного SignalR-канала (свёрнутое окно, фаервол, обрыв сети),
/// его сессия в БД остаётся живой — он сможет продолжить работу через REST в любой момент.
///
/// Одновременный вход в одну и ту же учётную запись с разных устройств запрещён на уровне
/// /api/Auth/login: сервер не выдаст новый токен, пока в Sessions есть запись для UserId.
/// </summary>
public class NotificationHub : Hub
{
    private readonly DbConnectionFactory _dbFactory;

    private static readonly ConcurrentDictionary<string, string> _sessionToConnection = new();
    private static readonly ConcurrentDictionary<string, (string Token, int UserId)> _connectionToSession = new();

    public NotificationHub(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();

        string? token = null;
        if (httpContext != null)
        {
            if (httpContext.Request.Headers.TryGetValue("X-Session-Id", out var headerVal))
                token = headerVal.ToString();
            if (string.IsNullOrEmpty(token))
                token = httpContext.Request.Query["sessionId"].ToString();
        }

        if (string.IsNullOrEmpty(token))
        {
            Context.Abort();
            return;
        }

        using var db = _dbFactory.Create();
        var session = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT UserId FROM Sessions WHERE Token = @Token",
            new { Token = token });

        if (session == null)
        {
            var termination = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT Reason, Message FROM SessionTerminationReasons
                  WHERE Token = @Token ORDER BY CreatedAt DESC LIMIT 1",
                new { Token = token });
            if (termination != null)
            {
                await Clients.Caller.SendAsync("SessionEnded",
                    (string)termination.reason, (string)termination.message);
                await db.ExecuteAsync(
                    "DELETE FROM SessionTerminationReasons WHERE Token = @Token",
                    new { Token = token });
            }
            else
            {
                await Clients.Caller.SendAsync("SessionEnded", "NoSession",
                    "Ваша сессия не найдена на сервере. Пожалуйста, войдите заново.");
            }
            Context.Abort();
            return;
        }

        int userId = (int)session.userid;

        // Если у того же токена уже есть SignalR-подключение (например, клиент переподключился
        // после обрыва сети) — забываем старое connection-id, заменяем на новое. Это нормальная
        // ситуация в рамках ОДНОЙ сессии, никак не связанная с двойным входом.
        if (_sessionToConnection.TryGetValue(token, out var oldConnId) && oldConnId != Context.ConnectionId)
        {
            _connectionToSession.TryRemove(oldConnId, out _);
        }

        _sessionToConnection[token] = Context.ConnectionId;
        _connectionToSession[Context.ConnectionId] = (token, userId);

        await db.ExecuteAsync(
            "UPDATE Sessions SET LastActivityAt = @Now WHERE Token = @Token",
            new { Now = DateTime.Now, Token = token });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionToSession.TryRemove(Context.ConnectionId, out var info))
        {
            if (_sessionToConnection.TryGetValue(info.Token, out var currentConn)
                && currentConn == Context.ConnectionId)
            {
                _sessionToConnection.TryRemove(info.Token, out _);
            }
            // Сессию в БД НЕ удаляем. Если клиент переподключится позже (или просто продолжит
            // работать через REST) — его сессия по-прежнему будет валидной.
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Метод оставлен для обратной совместимости с клиентом, но фактически ничего не делает,
    /// потому что отслеживание активности по heartbeat больше не используется.
    /// Старые клиенты могут продолжать его вызывать без ошибок.
    /// </summary>
    public Task Heartbeat()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Клиент сообщает серверу о намеренном выходе (закрытие программы пользователем).
    /// Этот канал — самый быстрый путь корректного завершения сессии: он не требует
    /// REST-запроса и отрабатывает даже на пути закрытия процесса. Это критично для
    /// работы запрета двойного входа — без корректного logout запись в Sessions
    /// останется в БД, и пользователь не сможет войти снова, пока администратор её
    /// не удалит вручную.
    /// </summary>
    public async Task ClientLogout()
    {
        if (!_connectionToSession.TryGetValue(Context.ConnectionId, out var info))
            return;

        try
        {
            using var db = _dbFactory.Create();
            await db.ExecuteAsync(
                "DELETE FROM Sessions WHERE Token = @Token",
                new { Token = info.Token });
        }
        catch { }

        if (_sessionToConnection.TryGetValue(info.Token, out var connId)
            && connId == Context.ConnectionId)
        {
            _sessionToConnection.TryRemove(info.Token, out _);
        }
        _connectionToSession.TryRemove(Context.ConnectionId, out _);
    }

    public static string? GetConnectionIdByToken(string token)
    {
        return _sessionToConnection.TryGetValue(token, out var conn) ? conn : null;
    }

    public static List<string> GetConnectionsByUserId(int userId)
    {
        var result = new List<string>();
        foreach (var kv in _connectionToSession)
        {
            if (kv.Value.UserId == userId)
                result.Add(kv.Key);
        }
        return result;
    }

    public static List<string> GetTokensByUserId(int userId)
    {
        var result = new List<string>();
        foreach (var kv in _connectionToSession)
        {
            if (kv.Value.UserId == userId)
                result.Add(kv.Value.Token);
        }
        return result;
    }

    public static void RemoveSession(string token)
    {
        if (_sessionToConnection.TryRemove(token, out var connId))
        {
            _connectionToSession.TryRemove(connId, out _);
        }
    }

    /// <summary>
    /// Возвращает true, если у указанного токена прямо сейчас есть активное SignalR-подключение.
    /// К жизненному циклу сессии в БД отношения не имеет — используется только во вспомогательных
    /// служебных проверках. Запрет двойного входа реализован через проверку самой записи в Sessions.
    /// </summary>
    public static bool HasActiveConnection(string token)
    {
        return _sessionToConnection.ContainsKey(token);
    }
}
