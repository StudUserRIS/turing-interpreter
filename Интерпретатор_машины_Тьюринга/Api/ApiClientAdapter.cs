using System;
using System.Threading.Tasks;

namespace Интерпретатор_машины_Тьюринга.Api
{
    /// <summary>
    /// Адаптер, превращающий статический <see cref="ApiClient"/> в реализацию
    /// контракта <see cref="IApiClient"/>.
    ///
    /// Назначение (OCP/DIP):
    ///   • Не ломая текущий статический API (которым пользуется большое число
    ///     окон в Form1.CourseWindows.cs), мы предоставляем экземплярный
    ///     контракт, который можно инжектировать в новые компоненты —
    ///     SessionManager, тесты, mock-сценарии.
    ///   • Все обращения адаптера делегируются статическим методам ApiClient,
    ///     поэтому единственный источник истины (HttpClient, SessionId,
    ///     CurrentUser) сохраняется.
    ///
    /// Это типовой паттерн «Adapter»: класс реализует требуемый интерфейс,
    /// а внутри обращается к существующей подсистеме без её изменения.
    /// </summary>
    public sealed class ApiClientAdapter : IApiClient
    {
        public static readonly IApiClient Default = new ApiClientAdapter();

        private ApiClientAdapter() { }

        public string SessionId => ApiClient.SessionId;
        public LoginResponse CurrentUser => ApiClient.CurrentUser;

        public event Action<ApiClient.ApiError> OnSessionEnded
        {
            add    { ApiClient.OnSessionEnded += value; }
            remove { ApiClient.OnSessionEnded -= value; }
        }

        public event Action<ApiClient.ApiError> OnConflict
        {
            add    { ApiClient.OnConflict += value; }
            remove { ApiClient.OnConflict -= value; }
        }

        public void Configure(string apiBaseUrl) => ApiClient.Configure(apiBaseUrl);

        public Task<(bool Success, ApiClient.ApiError Error)> LoginAsync(string login, string password)
            => ApiClient.LoginAsync(login, password);

        public Task LogoutAsync() => ApiClient.LogoutAsync();

        public async Task<bool> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            var result = await ApiClient.ChangePasswordAsync(oldPassword, newPassword);
            return result.Success;
        }

        public void ClearSessionLocally() => ApiClient.ClearSessionLocally();
    }
}
