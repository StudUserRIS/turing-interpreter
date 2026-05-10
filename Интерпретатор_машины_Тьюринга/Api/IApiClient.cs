using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Интерпретатор_машины_Тьюринга.Api
{
    /// <summary>
    /// Контракт сервиса работы с REST API сервера.
    ///
    /// Назначение интерфейса (DIP/OCP):
    ///   • Form1 и любые окна не должны напрямую зависеть от конкретной реализации
    ///     транспорта (HttpClient, конкретного <c>ApiClient</c>) — они должны
    ///     обращаться к абстракции.
    ///   • Это даёт возможность подменить реализацию (для unit-тестирования,
    ///     mock-сервера, альтернативного транспорта) без правки клиентского кода.
    ///   • Класс <see cref="Интерпретатор_машины_Тьюринга.ApiClient"/> остаётся
    ///     рабочим, но дополнительно регистрирует адаптер
    ///     <see cref="ApiClientAdapter"/>, реализующий данный контракт.
    ///
    /// В контракт вынесены только сценарии аутентификации и сессии — остальные
    /// методы (CRUD заданий/курсов/оценок) могут быть добавлены в интерфейс
    /// при дальнейшей миграции окон с прямого обращения к статическому классу.
    /// </summary>
    public interface IApiClient
    {
        // Идентификация текущей сессии
        string SessionId { get; }
        LoginResponse CurrentUser { get; }

        // События жизненного цикла сессии
        event Action<ApiClient.ApiError> OnSessionEnded;
        event Action<ApiClient.ApiError> OnConflict;

        // Конфигурация
        void Configure(string apiBaseUrl);

        // Авторизация
        Task<(bool Success, ApiClient.ApiError Error)> LoginAsync(string login, string password);
        Task LogoutAsync();
        Task<bool> ChangePasswordAsync(string oldPassword, string newPassword);

        // Сессионные операции
        void ClearSessionLocally();
    }
}
