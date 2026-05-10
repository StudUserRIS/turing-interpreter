using System;
using System.Threading.Tasks;
using Интерпретатор_машины_Тьюринга.Api;
using Интерпретатор_машины_Тьюринга.Core;

namespace Интерпретатор_машины_Тьюринга.Auth
{
    /// <summary>
    /// Менеджер серверной сессии пользователя.
    ///
    /// SRP: класс отвечает ИСКЛЮЧИТЕЛЬНО за бизнес-логику сессии —
    ///   • запуск/остановку SignalR-канала и подписку/отписку от его событий;
    ///   • хранение «последних известных» атрибутов профиля (login, ФИО, группа),
    ///     с которыми сравниваются push-обновления от сервера;
    ///   • быстрый «fire-and-forget» logout при закрытии формы / выключении ПК;
    ///   • публикацию доменных событий через <see cref="IDataRefreshBus"/>.
    ///
    /// Класс не содержит ни одного обращения к WinForms, не показывает диалогов,
    /// не правит UI. Это устраняет смешение «UI входа + логика сессии +
    /// проверка ролей в одном partial», которое было выявлено в Form1.AuthManager.cs.
    ///
    /// DIP: зависимости — только от абстракций (<see cref="IApiClient"/>,
    /// <see cref="IDataRefreshBus"/>) и от событий <see cref="SignalRClient"/>,
    /// который уже инкапсулирует свой транспорт.
    /// </summary>
    public sealed class SessionManager : IDisposable
    {
        private readonly IApiClient _api;
        private readonly IDataRefreshBus _bus;

        // «Последние известные» атрибуты профиля. Используются для определения,
        // какие именно поля сменились в push-уведомлении ProfileChanged.
        public string LastKnownLogin    { get; private set; }
        public string LastKnownFullName { get; private set; }
        public string LastKnownGroup    { get; private set; }

        public bool IsActive { get; private set; }

        // ──────────────────────────────────────────────────────────
        // События для UI-слоя.
        // UI подписывается на эти события и рисует диалоги/обновляет
        // меню/закрывает дочерние окна. Здесь же ни одного UI-вызова нет.
        // ──────────────────────────────────────────────────────────
        public event Action<string, string> SessionEnded;                  // (reason, message)
        public event Action<string, string, string> ProfileChanged;        // (fullName, login, group)
        public event Action<int, string, string> AssignmentChanged;        // (assignmentId, reason, message)
        public event Action<int, string, string> SubmissionChanged;        // (submissionId, reason, message)
        public event Action<string, string, int> DataChanged;              // (entity, action, id)

        public SessionManager(IApiClient api, IDataRefreshBus bus)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        // ──────────────────────────────────────────────────────────
        // Запуск heartbeat-сессии после успешного входа.
        // ──────────────────────────────────────────────────────────
        public async Task StartAsync()
        {
            Stop();

            LastKnownLogin    = _api.CurrentUser?.Login;
            LastKnownFullName = _api.CurrentUser?.FullName;
            LastKnownGroup    = _api.CurrentUser?.Group;

            SignalRClient.OnSessionEnded      += OnSignalRSessionEnded;
            SignalRClient.OnProfileChanged    += OnSignalRProfileChanged;
            SignalRClient.OnAssignmentChanged += OnSignalRAssignmentChanged;
            SignalRClient.OnSubmissionChanged += OnSignalRSubmissionChanged;
            SignalRClient.OnDataChanged       += OnSignalRDataChanged;

            IsActive = true;
            try { await SignalRClient.StartAsync(); } catch { }
        }

        // ──────────────────────────────────────────────────────────
        // Остановка heartbeat-сессии. Безопасна к повторному вызову.
        // ──────────────────────────────────────────────────────────
        public void Stop()
        {
            SignalRClient.OnSessionEnded      -= OnSignalRSessionEnded;
            SignalRClient.OnProfileChanged    -= OnSignalRProfileChanged;
            SignalRClient.OnAssignmentChanged -= OnSignalRAssignmentChanged;
            SignalRClient.OnSubmissionChanged -= OnSignalRSubmissionChanged;
            SignalRClient.OnDataChanged       -= OnSignalRDataChanged;

            try { _ = SignalRClient.StopAsync(); } catch { }
            LastKnownLogin = LastKnownFullName = LastKnownGroup = null;
            IsActive = false;
        }

        /// <summary>
        /// Обновляет «последний известный» снимок профиля после того,
        /// как UI обработал push-событие ProfileChanged.
        /// </summary>
        public void UpdateLastKnownProfile(string fullName, string login, string group)
        {
            LastKnownLogin    = login;
            LastKnownFullName = fullName;
            LastKnownGroup    = group;
        }

        // ──────────────────────────────────────────────────────────
        // Быстрый аварийный logout: закрытие формы или session-ending Windows.
        // Выполнение в строгом бюджете времени, без UI-блокировки.
        // ──────────────────────────────────────────────────────────
        public void FastFireAndForgetLogout(TimeSpan budget)
        {
            string token = _api.SessionId;
            if (string.IsNullOrEmpty(token)) return;

            try { _api.ClearSessionLocally(); } catch { }
            try { Stop(); } catch { }

            using (var cts = new System.Threading.CancellationTokenSource(budget))
            {
                Task logoutTask = SignalRClient.IsConnected
                    ? SignalRClient.SendLogoutAsync(cts.Token)
                    : SendRestLogoutAsync(token, cts.Token);
                try { logoutTask.Wait(budget); } catch { }
            }

            try { SignalRClient.AbandonConnection(); } catch { }
            try { ApiClient.CurrentUser = null; } catch { }
        }

        private static async Task SendRestLogoutAsync(string sessionToken, System.Threading.CancellationToken ct)
        {
            try
            {
                var settings = ConnectionSettings.Load();
                string baseUrl = settings.ApiBaseUrl;
                if (string.IsNullOrEmpty(baseUrl)) return;
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
                    http.DefaultRequestHeaders.Add("X-Session-Id", sessionToken);
                    using (var req = new System.Net.Http.HttpRequestMessage(
                                          System.Net.Http.HttpMethod.Post, baseUrl + "Auth/logout"))
                    {
                        await http.SendAsync(req, ct);
                    }
                }
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        // SignalR push → доменные события для UI и шины обновления
        // ──────────────────────────────────────────────────────────
        private void OnSignalRSessionEnded(string reason, string message)
            => SessionEnded?.Invoke(reason, message);

        private void OnSignalRProfileChanged(string fullName, string login, string group)
            => ProfileChanged?.Invoke(fullName, login, group);

        private void OnSignalRAssignmentChanged(int assignmentId, string reason, string message)
            => AssignmentChanged?.Invoke(assignmentId, reason, message);

        private void OnSignalRSubmissionChanged(int submissionId, string reason, string message)
        {
            SubmissionChanged?.Invoke(submissionId, reason, message);
            _bus.Raise("Submission", "Updated", submissionId);
        }

        private void OnSignalRDataChanged(string entity, string action, int id)
        {
            DataChanged?.Invoke(entity, action, id);
            _bus.Raise(entity, action, id);
        }

        public void Dispose() => Stop();
    }
}
