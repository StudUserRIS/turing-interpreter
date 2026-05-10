using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Интерпретатор_машины_Тьюринга
{
    public static class SignalRClient
    {
        private static HubConnection _connection;

        public static event Action<string, string> OnSessionEnded;
        public static event Action<string, string, string> OnProfileChanged;
        public static event Action<int, string, string> OnAssignmentChanged;
        public static event Action<int, string, string> OnSubmissionChanged;

        /// <summary>
        /// Универсальное событие об изменении табличных данных в системе.
        /// Подписчики (открытые окна "Мои курсы", "Курсы", "Администрирование", "Проверка работ"
        /// и т.п.) при срабатывании этого события автоматически перезагружают свои данные —
        /// фактически как при нажатии кнопки "🔄 Обновить", но без участия пользователя.
        ///
        /// Параметры:
        ///   • entity — тип сущности: "Group", "Course", "Assignment", "Submission",
        ///     "Student", "Teacher", "CourseGroups", "GradingPolicy".
        ///   • action — что произошло: "Created", "Updated", "Deleted".
        ///   • id     — идентификатор сущности (если применимо), иначе 0.
        /// </summary>
        public static event Action<string, string, int> OnDataChanged;

        public static bool IsConnected => _connection != null && _connection.State == HubConnectionState.Connected;

        /// <summary>
        /// Запустить SignalR-подключение. URL берётся из ConnectionSettings (а тот — из App.config).
        /// При нестабильной сети — автоматический реконнект и увеличенные таймауты.
        ///
        /// ВАЖНО: SignalR используется ТОЛЬКО для доставки push-уведомлений с сервера
        /// (вытеснение сессии админом, изменение профиля, изменение задания, изменение
        /// табличных данных). Жизненный цикл сессии от состояния SignalR-канала больше
        /// НЕ зависит — сессия остаётся валидной даже если SignalR не подключился
        /// (фаервол, прокси и т.п.).
        ///
        /// КРИТИЧЕСКИ ВАЖНО: sessionId передаётся через query-параметр URL, а не через
        /// заголовок X-Session-Id. Причина: WebSocket-транспорт (используется по умолчанию)
        /// НЕ ПЕРЕДАЁТ кастомные HTTP-заголовки при апгрейде соединения. Если передавать
        /// sessionId только через Headers, при использовании WebSocket сервер не получит
        /// токен, и подключение будет отклонено в OnConnectedAsync. В результате push-
        /// уведомления (включая "Ваша учётная запись удалена") не будут доходить до клиента
        /// мгновенно — пользователь увидит сообщение только при следующем REST-запросе.
        /// Query-параметр работает на ВСЕХ транспортах (WebSocket, ServerSentEvents,
        /// LongPolling), поэтому используем его как основной способ передачи токена.
        /// Дополнительно дублируем токен в заголовке — на случай работы под прокси,
        /// которые могут переписывать query-string.
        /// </summary>
        public static async Task StartAsync()
        {
            await StopAsync();

            var settings = ConnectionSettings.Load();
            string hubUrl = settings.SignalRHubUrl;
            if (string.IsNullOrEmpty(hubUrl)) return;

            string sessionId = ApiClient.SessionId ?? "";
            if (string.IsNullOrEmpty(sessionId)) return;

            // Добавляем sessionId в query-string. Это гарантирует, что сервер получит
            // токен на любом транспорте, включая WebSocket.
            string urlWithToken = hubUrl;
            if (urlWithToken.Contains("?"))
                urlWithToken += "&sessionId=" + Uri.EscapeDataString(sessionId);
            else
                urlWithToken += "?sessionId=" + Uri.EscapeDataString(sessionId);

            _connection = new HubConnectionBuilder()
                .WithUrl(urlWithToken, options =>
                {
                    // Дублируем токен в заголовке — для совместимости со сценариями,
                    // когда работает не WebSocket, а LongPolling/ServerSentEvents,
                    // а также как дополнительный канал на случай прокси.
                    options.Headers.Add("X-Session-Id", sessionId);
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(20),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(60)
                })
                .Build();

            _connection.ServerTimeout = TimeSpan.FromSeconds(120);
            _connection.HandshakeTimeout = TimeSpan.FromSeconds(30);
            _connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            _connection.On<string, string>("SessionEnded", (reason, message) =>
            {
                try { OnSessionEnded?.Invoke(reason, message); } catch { }
            });

            _connection.On<string, string, string>("ProfileChanged", (fullName, login, group) =>
            {
                try { OnProfileChanged?.Invoke(fullName, login, group); } catch { }
            });

            _connection.On<int, string, string>("AssignmentChanged", (assignmentId, reason, message) =>
            {
                try { OnAssignmentChanged?.Invoke(assignmentId, reason, message); } catch { }
            });

            _connection.On<int, string, string>("SubmissionChanged", (submissionId, reason, message) =>
            {
                try { OnSubmissionChanged?.Invoke(submissionId, reason, message); } catch { }
            });

            // Универсальный канал обновления табличных данных. Сервер шлёт его после
            // успешного create/update/delete для любой сущности (группы, курса, задания,
            // решения, пользователя). Клиент сам решает, какие открытые окна обновить.
            _connection.On<string, string, int>("DataChanged", (entity, action, id) =>
            {
                try { OnDataChanged?.Invoke(entity ?? "", action ?? "", id); } catch { }
            });

            _connection.Closed += async (error) =>
            {
                await Task.CompletedTask;
            };

            try
            {
                await _connection.StartAsync();
            }
            catch
            {
                // Если SignalR не поднялся — основной REST-канал продолжит работать.
                // Пользователь сможет нормально работать в системе, просто не получит
                // мгновенных push-уведомлений от администратора, пока канал не восстановится.
            }
        }

        /// <summary>
        /// Корректное закрытие SignalR-соединения с короткими таймаутами.
        /// Используется в обычных сценариях (logout пользователя через меню), когда
        /// можно подождать полноценный graceful shutdown.
        /// </summary>
        public static async Task StopAsync()
        {
            if (_connection != null)
            {
                try { await _connection.StopAsync(); } catch { }
                try { await _connection.DisposeAsync(); } catch { }
                _connection = null;
            }
        }

        /// <summary>
        /// Быстрое принудительное прекращение SignalR-соединения для сценария закрытия программы.
        /// Не ждёт graceful shutdown библиотеки SignalR — просто обрывает ссылку и фоном
        /// запускает Dispose, чтобы не блокировать UI-поток. Серверная сторона корректно
        /// отработает разрыв через NotificationHub.OnDisconnectedAsync.
        /// </summary>
        public static void AbandonConnection()
        {
            var conn = _connection;
            _connection = null;
            if (conn == null) return;

            // Запускаем освобождение ресурсов в фоне, но НЕ ждём его завершения.
            // Если процесс закроется раньше — это нормально, ОС освободит дескрипторы.
            _ = Task.Run(async () =>
            {
                try { await conn.DisposeAsync(); } catch { }
            });
        }

        /// <summary>
        /// Уведомить сервер через SignalR-хаб о намеренном выходе клиента (закрытие программы).
        /// Серверная сторона немедленно удалит сессию из БД.
        /// Параметр cancellationToken позволяет вызывающему коду ограничить время ожидания.
        /// </summary>
        public static async Task SendLogoutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_connection != null && _connection.State == HubConnectionState.Connected)
                {
                    await _connection.InvokeAsync("ClientLogout", cancellationToken);
                }
            }
            catch { }
        }
    }
}
