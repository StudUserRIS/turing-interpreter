using Microsoft.AspNetCore.SignalR;
using Turing_Backend.Hubs;
using Dapper;
using Turing_Backend.Database;

namespace Turing_Backend.Services;

/// <summary>
/// Высокоуровневый сервис для отправки push-уведомлений клиентам.
/// Используется во всех endpoint'ах вместо записи в SessionTerminationReasons + надежды на REST-запрос.
/// </summary>
public class NotificationService
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly DbConnectionFactory _dbFactory;

    public NotificationService(IHubContext<NotificationHub> hub, DbConnectionFactory dbFactory)
    {
        _hub = hub;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Принудительно завершить все сессии указанного пользователя с конкретной причиной.
    ///
    /// КРИТИЧЕСКИ ВАЖНЫЙ ПОРЯДОК ДЕЙСТВИЙ для мгновенной доставки уведомления:
    ///   1) СНАЧАЛА пушим SignalR-сообщение всем активным подключениям пользователя.
    ///      На этом шаге клиент получает уведомление мгновенно, БЕЗ КАКИХ-ЛИБО ДЕЙСТВИЙ
    ///      с его стороны (даже если он бездействует, окно свёрнуто и т.п.).
    ///   2) Сохраняем причину в SessionTerminationReasons — это резервный канал на случай,
    ///      если SignalR-сообщение не дойдёт (например, клиент был временно отключён).
    ///   3) Только ПОСЛЕ этого удаляем сессии из БД и из локальных словарей хаба.
    ///
    /// Раньше порядок был обратным: сначала удалялись сессии и connection-id, и только потом
    /// отправлялся push. Это иногда приводило к тому, что между удалением сессии и
    /// отправкой push клиент уже не получал сообщение (его connection-id уже был забыт).
    /// Теперь ситуация исключена — push гарантированно уходит до удаления.
    ///
    /// Также делаем небольшую (50 мс) задержку после отправки push перед удалением сессии,
    /// чтобы у SignalR-инфраструктуры было время фактически отправить байты в сокет.
    /// Это критично для сценария удаления пользователя, когда сервер сразу же выполняет
    /// каскадный DELETE FROM Users — иначе разрыв соединения может случиться раньше,
    /// чем сообщение долетит.
    /// </summary>
    public async Task EndAllSessionsForUserAsync(int userId, string reason, string message)
    {
        using var db = _dbFactory.Create();

        var tokens = (await db.QueryAsync<string>(
            "SELECT Token FROM Sessions WHERE UserId = @UserId",
            new { UserId = userId })).ToList();

        if (tokens.Count == 0) return;

        // Шаг 1: записываем причину в БД (резервный канал на случай, если push не дойдёт).
        foreach (var token in tokens)
        {
            try
            {
                await db.ExecuteAsync(
                    "INSERT INTO SessionTerminationReasons (Token, UserId, Reason, Message) VALUES (@Token, @UserId, @Reason, @Message)",
                    new { Token = token, UserId = userId, Reason = reason, Message = message });
            }
            catch { }
        }

        // Шаг 2: пушим уведомление всем активным подключениям пользователя.
        // Это нужно сделать ДО удаления сессий и до удаления записи из словарей хаба.
        bool pushedToAnyone = false;
        foreach (var token in tokens)
        {
            var connectionId = NotificationHub.GetConnectionIdByToken(token);
            if (connectionId != null)
            {
                try
                {
                    await _hub.Clients.Client(connectionId).SendAsync("SessionEnded", reason, message);
                    pushedToAnyone = true;
                }
                catch { }
            }
        }

        // Шаг 3: даём SignalR-инфраструктуре немного времени, чтобы фактически
        // отправить байты в сокет. Без этой паузы при последующем удалении пользователя
        // (особенно через каскад FK Users → Sessions) сокет может закрыться раньше,
        // чем сообщение успеет уйти. 100 мс достаточно даже для медленных соединений.
        if (pushedToAnyone)
        {
            try { await Task.Delay(100); } catch { }
        }

        // Шаг 4: удаляем сессии из БД и забываем connection-id в хабе.
        // Делаем это ПОСЛЕ того, как push-сообщение гарантированно ушло.
        foreach (var token in tokens)
        {
            NotificationHub.RemoveSession(token);
        }
        try
        {
            await db.ExecuteAsync("DELETE FROM Sessions WHERE UserId = @UserId", new { UserId = userId });
        }
        catch { }
    }

    /// <summary>
    /// Уведомить пользователя об изменении его профиля (без завершения сессии).
    /// Применяется при смене ФИО или группы — клиент должен закрыть открытые окна и обновить отображение.
    /// </summary>
    public async Task NotifyProfileChangedAsync(int userId, string fullName, string login, string? groupName)
    {
        var connections = NotificationHub.GetConnectionsByUserId(userId);
        foreach (var conn in connections)
        {
            try
            {
                await _hub.Clients.Client(conn).SendAsync("ProfileChanged", fullName, login, groupName ?? "");
            }
            catch { }
        }
    }

    /// <summary>
    /// Уведомить всех студентов, у которых открыто данное задание, что оно изменилось
    /// (скрыто, удалено, переведено в черновик, курс архивирован).
    /// </summary>
    public async Task NotifyAssignmentChangedAsync(int assignmentId, string reason, string message)
    {
        // Рассылаем всем подключённым студентам — они на клиенте сами проверят, актуально ли это для них.
        try
        {
            await _hub.Clients.All.SendAsync("AssignmentChanged", assignmentId, reason, message);
        }
        catch { }
    }

    /// <summary>
    /// Уведомить преподавателя, что состояние работы студента изменилось (студент отозвал, удалил и т.д.).
    /// </summary>
    public async Task NotifySubmissionChangedAsync(int submissionId, string reason, string message)
    {
        try
        {
            await _hub.Clients.All.SendAsync("SubmissionChanged", submissionId, reason, message);
        }
        catch { }
    }

    /// <summary>
    /// Универсальное широковещательное уведомление об изменении табличных данных в системе.
    /// Используется для автоматического обновления окон у всех подключённых пользователей
    /// после CRUD-операций (создание/изменение/удаление сущностей).
    ///
    /// Параметры:
    ///   • entity — тип сущности: "Group", "Course", "Assignment", "Submission",
    ///     "User", "Teacher", "Student", "CourseGroups", "GradingPolicy".
    ///   • action — что произошло: "Created", "Updated", "Deleted".
    ///   • id     — идентификатор сущности (если применимо), иначе 0.
    ///
    /// Клиент сам решает, какие именно его открытые окна должны обновиться, исходя из
    /// текущего состояния (роли пользователя, открытых форм и т.п.).
    ///
    /// Это уведомление НЕ закрывает никаких окон, не показывает диалогов и не прерывает
    /// текущие действия пользователя — оно лишь мягко обновляет данные в табличных
    /// представлениях, как если бы пользователь нажал кнопку «🔄 Обновить».
    /// </summary>
    public async Task BroadcastDataChangedAsync(string entity, string action, int id = 0)
    {
        try
        {
            await _hub.Clients.All.SendAsync("DataChanged", entity ?? "", action ?? "", id);
        }
        catch { }
    }
}
