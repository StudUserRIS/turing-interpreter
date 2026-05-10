using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Интерпретатор_машины_Тьюринга
{
    public static class ApiClient
    {
        public class ApiError
        {
            public string Reason { get; set; } = "";
            public string Message { get; set; } = "";
            public Newtonsoft.Json.Linq.JToken? CurrentData { get; set; } = null;
            public int StatusCode { get; set; }
            // Если true — окно уже было показано/обработано вышестоящим кодом,
            // дополнительное сообщение от вызывающей стороны показывать НЕ нужно.
            public bool Handled { get; set; }
        }

        // Явный результат для админских операций. Используется во всех окнах Администрирования,
        // чтобы вызывающий код мог сам решить, какое окно показать (или не показать вовсе).
        // Никаких автоматических Toasts/MessageBox внутри этих операций нет — окно показывает только UI.
        public enum AdminOpStatus
        {
            Success,        // операция прошла успешно
            Conflict,       // 409/410 — данные устарели или удалены другим пользователем
            DuplicateName,  // 409 с Reason="DuplicateName" — занятое имя (логин/группа/курс/задание)
            ValidationError,// 400 — валидация на стороне сервера
            NetworkError,   // нет связи / таймаут / 500
            SessionEnded,   // 401 — сессия завершена (показано общее окно SessionEnded)
            Unknown
        }


        public class AdminOpResult
        {
            public AdminOpStatus Status { get; set; } = AdminOpStatus.Unknown;
            public string Reason { get; set; } = "";
            public string Message { get; set; } = "";

            public bool IsSuccess => Status == AdminOpStatus.Success;

            // ВАЖНО: «обычный» конфликт версий/удаления — это всё, что НЕ является DuplicateName.
            // Раньше IsConflict возвращал true и для DuplicateName, из-за чего вызывающий UI,
            // если случайно проверял IsConflict раньше IsDuplicateName, закрывал окно через
            // ShowAdminStaleDataDialog. Теперь IsConflict гарантированно НЕ срабатывает для
            // DuplicateName — UI может проверять любое из этих свойств в любом порядке.
            public bool IsConflict => Status == AdminOpStatus.Conflict;

            // Удобный флаг для UI: имя/логин занят. Сообщение брать из Message.
            public bool IsDuplicateName => Status == AdminOpStatus.DuplicateName;
        }



        private static HttpClient client = CreateHttpClient();
        private static string BaseUrl = string.Empty;

        public static string SessionId { get; private set; }
        public static LoginResponse CurrentUser { get; set; }

        public static event Action<ApiError> OnSessionEnded;
        public static event Action<ApiError> OnConflict;

        // Флаг: если true — события OnSessionEnded/OnConflict не пробрасываются наружу
        // (используется для фоновых/служебных запросов, чтобы не плодить окна).
        [ThreadStatic]
        private static bool _silentMode;

        public static IDisposable SilentScope()
        {
            return new SilentToken();
        }

        private class SilentToken : IDisposable
        {
            private readonly bool _previous;
            public SilentToken() { _previous = _silentMode; _silentMode = true; }
            public void Dispose() { _silentMode = _previous; }
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            var c = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            c.DefaultRequestHeaders.ExpectContinue = false;
            c.DefaultRequestHeaders.ConnectionClose = false;
            return c;
        }

        public static void Configure(string apiBaseUrl)
        {
            BaseUrl = apiBaseUrl ?? "";
            try { client?.Dispose(); } catch { }
            client = CreateHttpClient();
        }

        private static void EnsureBaseUrl()
        {
            if (string.IsNullOrEmpty(BaseUrl))
            {
                var settings = ConnectionSettings.Load();
                BaseUrl = settings.ApiBaseUrl;
                if (string.IsNullOrEmpty(BaseUrl))
                {
                    BaseUrl = "http://localhost:5007/api/";
                }
            }
        }

        /// <summary>
        /// Разбирает тело HTTP-ответа и извлекает Reason/Message/CurrentData/StatusCode.
        ///
        /// КРИТИЧЕСКИ ВАЖНО: тело HttpResponseMessage можно безопасно прочитать только один раз.
        /// SendWithSessionAsync для всех 401/409/410 уже читает тело и ПРИСОЕДИНЯЕТ разобранный
        /// объект к response через AttachParsedError. Поэтому здесь сначала пробуем достать
        /// уже готовый ApiError — это предотвращает «потерю» Reason при повторном вызове
        /// (повторный ReadAsStringAsync на уже прочитанном Content возвращает пустую строку,
        /// из-за чего Reason оказывается пустым и UI показывает обобщённое «Сервер вернул ошибку»
        /// вместо корректного «Имя уже занято» / «Курс был удалён» и т.п.).
        /// </summary>
        /// <summary>
        /// Разбирает тело HTTP-ответа и извлекает Reason/Message/CurrentData/StatusCode.
        ///
        /// КРИТИЧЕСКИ ВАЖНО: тело HttpResponseMessage можно безопасно прочитать только один раз.
        /// SendWithSessionAsync для всех 401/409/410 уже читает тело и ПРИСОЕДИНЯЕТ разобранный
        /// объект к response через AttachParsedError. Поэтому здесь сначала пробуем достать
        /// уже готовый ApiError — это предотвращает «потерю» Reason при повторном вызове.
        ///
        /// ОТДЕЛЬНО ВАЖНО: серверный Results.Json в .NET 8 может сериализовать поля либо в
        /// PascalCase ("Reason"/"Message"), либо в camelCase ("reason"/"message") — зависит
        /// от глобальной политики JsonSerializerOptions. Поэтому здесь читаем поля
        /// case-insensitive, перебирая все возможные варианты имён. Это страхует клиент
        /// от любых будущих изменений конфигурации сериализатора на сервере.
        /// </summary>
        public static async Task<ApiError> ParseError(HttpResponseMessage response)
        {
            if (response == null)
                return new ApiError { StatusCode = 0 };

            // Если ответ уже был разобран (например, в SendWithSessionAsync) — возвращаем
            // тот же объект, не пытаясь повторно читать Content.
            var attached = TryGetAttachedError(response);
            if (attached != null)
                return attached;

            var err = new ApiError { StatusCode = (int)response.StatusCode };
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var parsed = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(body);
                    err.Reason = ReadJsonString(parsed, "Reason", "reason") ?? "";
                    err.Message = ReadJsonString(parsed, "Message", "message") ?? "";
                    err.CurrentData = ReadJsonToken(parsed, "CurrentData", "currentData");
                    if (string.IsNullOrEmpty(err.Message))
                    {
                        err.Message = ReadJsonString(parsed, "Error", "error") ?? "";
                    }
                }
            }
            catch { }

            // Сохраняем разобранный объект на response, чтобы последующие вызовы
            // ParseError/HandleErrorAsync видели тот же результат.
            AttachParsedError(response, err);
            return err;
        }


        public static async Task LogoutAsync()
        {
            EnsureBaseUrl();
            try { await SignalRClient.StopAsync(); } catch { }
            if (!string.IsNullOrEmpty(SessionId))
            {
                try
                {
                    using (SilentScope())
                    {
                        await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Auth/logout"));
                    }
                }
                catch { }
            }
            CurrentUser = null;
            SessionId = null;
        }

        public static async Task<(bool Success, ApiError Error)> LoginAsync(string login, string password)
        {
            EnsureBaseUrl();
            var loginData = new { Login = login, Password = password };
            var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(BaseUrl + "Auth/login", content);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    CurrentUser = JsonConvert.DeserializeObject<LoginResponse>(responseBody);
                    SessionId = CurrentUser.SessionId;
                    return (true, null);
                }
                var err = await ParseError(response);
                if (string.IsNullOrEmpty(err.Message))
                {
                    switch ((int)response.StatusCode)
                    {
                        case 401: err.Message = "Неверный логин или пароль. Проверьте правильность ввода и повторите попытку."; err.Reason = "InvalidCredentials"; break;
                        case 409: err.Message = "В эту учётную запись уже выполнен вход с другого устройства. Одновременная работа в одной учётной записи с разных устройств запрещена."; err.Reason = "AlreadyLoggedIn"; break;
                        case 429: err.Message = "Слишком много неудачных попыток входа. Попробуйте позже."; err.Reason = "TooManyAttempts"; break;
                        default: err.Message = "Сервер недоступен или вернул неожиданный ответ."; break;
                    }
                }
                return (false, err);
            }
            catch (TaskCanceledException)
            {
                return (false, new ApiError
                {
                    Reason = "NetworkError",
                    Message = "Превышено время ожидания ответа от сервера. Проверьте интернет-соединение."
                });
            }
            catch (HttpRequestException ex)
            {
                return (false, new ApiError
                {
                    Reason = "NetworkError",
                    Message = "Не удалось подключиться к серверу. Проверьте, что сервер запущен.\n\nТехническая информация: " + ex.Message
                });
            }
            catch (Exception ex)
            {
                return (false, new ApiError
                {
                    Reason = "NetworkError",
                    Message = "Произошла ошибка соединения: " + ex.Message
                });
            }
        }

        public static void Logout()
        {
            CurrentUser = null;
            SessionId = null;
        }

        public static void ClearSessionLocally()
        {
            SessionId = null;
            CurrentUser = null;
        }

        private static async Task<HttpResponseMessage> SendWithSessionAsync(HttpRequestMessage request)
        {
            string snapshotSessionId = SessionId ?? "";
            if (!string.IsNullOrEmpty(snapshotSessionId))
                request.Headers.Add("X-Session-Id", snapshotSessionId);

            // Снимок silent-режима ДО await — чтобы не зависеть от смены потока внутри
            // ConfigureAwait/SynchronizationContext (флаг помечен как [ThreadStatic]
            // и теоретически может «потеряться» после возобновления продолжения на другом потоке).
            bool silentSnapshot = _silentMode;

            try
            {
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    int code = (int)response.StatusCode;
                    if (code == 401 || code == 409 || code == 410)
                    {
                        // ВНИМАНИЕ: здесь мы читаем тело ответа сразу и сохраняем результат на
                        // сам response через AttachParsedError. Любые последующие вызовы
                        // ParseError(response) получат тот же объект без повторного чтения.
                        //
                        // Поля JSON читаем case-insensitive — сервер может сериализовать
                        // их в PascalCase или camelCase в зависимости от глобальной политики
                        // System.Text.Json. Из-за рассинхронизации регистров клиент раньше
                        // получал пустой Reason и трактовал любую 409 как VersionConflict —
                        // и пользователь видел "Данные окна не являются актуальными" вместо
                        // "Логин уже занят". Теперь это исправлено.
                        var err = new ApiError { StatusCode = code };
                        try
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                var parsed = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(body);
                                err.Reason = ReadJsonString(parsed, "Reason", "reason") ?? "";
                                err.Message = ReadJsonString(parsed, "Message", "message") ?? "";
                                err.CurrentData = ReadJsonToken(parsed, "CurrentData", "currentData");
                                if (string.IsNullOrEmpty(err.Message))
                                    err.Message = ReadJsonString(parsed, "Error", "error") ?? "";
                            }
                        }
                        catch { }

                        // КРИТИЧЕСКИ ВАЖНО: прикрепляем разобранный ApiError СРАЗУ — ещё ДО того,
                        // как поднимем глобальное событие OnConflict. Иначе обработчик HandleConflict
                        // (на UI-потоке) и параллельный BuildAdminResultAsync могут конкурировать
                        // за чтение тела ответа и видеть пустой Reason.
                        AttachParsedError(response, err);

                        bool sessionAlreadyCleared = string.IsNullOrEmpty(SessionId)
                                                     || !string.Equals(SessionId, snapshotSessionId, StringComparison.Ordinal);

                        if (!silentSnapshot)
                        {
                            if (code == 401 && !string.IsNullOrEmpty(err.Reason))
                            {
                                if (!sessionAlreadyCleared)
                                {
                                    err.Handled = true;
                                    try { OnSessionEnded?.Invoke(err); } catch { }
                                }
                                else
                                {
                                    err.Handled = true;
                                }
                            }
                            else if ((code == 409 || code == 410) && !string.IsNullOrEmpty(err.Reason))
                            {
                                // ВАЖНО: ошибки "имя/логин занят" (DuplicateName) НЕ являются
                                // конфликтом версий или признаком устаревших данных. Это обычная
                                // валидационная ошибка, которую вызывающий код должен показать сам
                                // конкретным понятным сообщением ("Логин уже занят", "Группа с таким
                                // названием уже существует" и т.п.) и НЕ закрывать при этом окно.
                                // Поэтому глобальное событие OnConflict для DuplicateName НЕ дёргаем
                                // — иначе поверх корректного сообщения всплывёт второе окно "Данные устарели".
                                if (!IsDuplicateNameReason(err.Reason))
                                {
                                    err.Handled = true;
                                    try { OnConflict?.Invoke(err); } catch { }
                                }
                            }
                        }
                    }
                }
                return response;
            }
            catch (TaskCanceledException)
            {
                var resp = new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout);
                AttachParsedError(resp, new ApiError
                {
                    StatusCode = 408,
                    Reason = "NetworkError",
                    Message = "Превышено время ожидания ответа от сервера. Проверьте интернет-соединение."
                });
                return resp;
            }
            catch (HttpRequestException ex)
            {
                var resp = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
                AttachParsedError(resp, new ApiError
                {
                    StatusCode = 503,
                    Reason = "NetworkError",
                    Message = "Сервер временно недоступен. Проверьте подключение к интернету.\n\nТехническая информация: " + ex.Message
                });
                return resp;
            }
            catch (Exception ex)
            {
                var resp = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                AttachParsedError(resp, new ApiError
                {
                    StatusCode = 500,
                    Reason = "NetworkError",
                    Message = "Произошла ошибка соединения: " + ex.Message
                });
                return resp;
            }
        }




        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpResponseMessage, ApiError> _parsedErrors
            = new System.Runtime.CompilerServices.ConditionalWeakTable<HttpResponseMessage, ApiError>();

        private static void AttachParsedError(HttpResponseMessage response, ApiError err)
        {
            try
            {
                _parsedErrors.Remove(response);
                _parsedErrors.Add(response, err);
            }
            catch { }
        }
        /// <summary>
        /// Достаёт строковое значение поля JSON, перебирая все возможные регистры имени.
        /// Нужен потому, что серверный сериализатор в .NET 8 может выдавать имена
        /// либо в PascalCase, либо в camelCase в зависимости от настроек.
        /// </summary>
        private static string ReadJsonString(Newtonsoft.Json.Linq.JObject obj, params string[] candidateNames)
        {
            if (obj == null) return null;
            foreach (var name in candidateNames)
            {
                var token = obj[name];
                if (token != null && token.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    string s = token.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            // На случай экзотических кейсов — поиск по имени без учёта регистра.
            foreach (var prop in obj.Properties())
            {
                foreach (var name in candidateNames)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        var token = prop.Value;
                        if (token != null && token.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                        {
                            string s = token.ToString();
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Достаёт произвольный JToken по имени поля, перебирая все возможные регистры.
        /// </summary>
        private static Newtonsoft.Json.Linq.JToken ReadJsonToken(Newtonsoft.Json.Linq.JObject obj, params string[] candidateNames)
        {
            if (obj == null) return null;
            foreach (var name in candidateNames)
            {
                var token = obj[name];
                if (token != null && token.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    return token;
            }
            foreach (var prop in obj.Properties())
            {
                foreach (var name in candidateNames)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        var token = prop.Value;
                        if (token != null && token.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                            return token;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Безопасная проверка «причина = занятое имя» (DuplicateName), которая работает
        /// независимо от регистра самого значения. Сервер всегда шлёт строго "DuplicateName",
        /// но дополнительная страховка не повредит — это ключевая ветка, ошибки в которой
        /// пользователю обходятся «принудительным закрытием окна».
        /// </summary>
        private static bool IsDuplicateNameReason(string reason)
        {
            return !string.IsNullOrEmpty(reason)
                && string.Equals(reason, "DuplicateName", StringComparison.OrdinalIgnoreCase);
        }

        private static ApiError TryGetAttachedError(HttpResponseMessage response)
        {
            if (response == null) return null;
            if (_parsedErrors.TryGetValue(response, out var err)) return err;
            return null;
        }

        public static async Task<bool> HandleErrorAsync(HttpResponseMessage response)
        {
            if (response == null) return false;
            if (response.IsSuccessStatusCode) return true;

            var err = TryGetAttachedError(response) ?? await ParseError(response);

            if (err.Handled) return false;
            if (_silentMode) return false;

            int code = (int)response.StatusCode;

            // Дубликат имени/логина — это валидационная ошибка, а не "конфликт данных".
            // Не показываем её через общее окно OnConflict. Вызывающий код сам разберёт
            // эту ситуацию (через AdminOpResult.IsDuplicateName или через ParseError).
            // Сравнение case-insensitive — на случай, если сервер при изменении конфигурации
            // сериализатора вернёт значение в другом регистре.
            if ((code == 409 || code == 410) && IsDuplicateNameReason(err.Reason))
            {
                err.Handled = true;
                return false;
            }

            if (string.IsNullOrEmpty(err.Reason))
            {
                err.Reason = "ServerError";
            }
            if (string.IsNullOrEmpty(err.Message))
            {
                err.Message = code == 408 || code == 503
                    ? "Сервер временно недоступен. Проверьте подключение к интернету."
                    : "Сервер вернул ошибку. Попробуйте повторить операцию.";
            }

            err.Handled = true;
            try { OnConflict?.Invoke(err); } catch { }
            return false;
        }



        // Внутренний помощник: выполняет запрос в SilentScope и возвращает явный результат
        // для админских операций. Никаких побочных окон не показывает — окно покажет вызывающий UI.
        private static async Task<AdminOpResult> ExecuteAdminRequestAsync(HttpRequestMessage request)
        {
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(request);
            }
            return await BuildAdminResultAsync(response);
        }

        private static async Task<AdminOpResult> BuildAdminResultAsync(HttpResponseMessage response)
        {
            if (response == null)
                return new AdminOpResult { Status = AdminOpStatus.Unknown, Message = "Нет ответа от сервера." };

            if (response.IsSuccessStatusCode)
                return new AdminOpResult { Status = AdminOpStatus.Success };

            int code = (int)response.StatusCode;
            var err = TryGetAttachedError(response) ?? await ParseError(response);

            if (code == 401)
                return new AdminOpResult { Status = AdminOpStatus.SessionEnded, Reason = err.Reason ?? "", Message = err.Message ?? "" };

            // 409/410 с Reason="DuplicateName" — это особая ошибка, которую UI должен показать
            // как обычное предупреждение (логин/имя занят), не закрывая окно редактирования.
            // Все остальные 409/410 интерпретируются как «данные устарели/удалены»
            // и закрывают окно с сообщением ShowAdminStaleDataDialog.
            //
            // Сравнение Reason ведём case-insensitive (см. IsDuplicateNameReason): сервер всегда
            // присылает строго "DuplicateName", но клиент должен быть устойчив к любым
            // вариациям сериализации, чтобы пользователь никогда не увидел "Данные окна
            // не являются актуальными" вместо корректного "Имя/логин уже занят".
            if ((code == 409 || code == 410) && IsDuplicateNameReason(err.Reason))
                return new AdminOpResult { Status = AdminOpStatus.DuplicateName, Reason = err.Reason ?? "", Message = err.Message ?? "" };

            if (code == 409 || code == 410)
                return new AdminOpResult { Status = AdminOpStatus.Conflict, Reason = err.Reason ?? "", Message = err.Message ?? "" };

            if (code == 400)
                return new AdminOpResult { Status = AdminOpStatus.ValidationError, Reason = err.Reason ?? "", Message = err.Message ?? "" };

            if (code == 408 || code == 503 || code == 500)
                return new AdminOpResult { Status = AdminOpStatus.NetworkError, Reason = err.Reason ?? "", Message = err.Message ?? "" };

            return new AdminOpResult { Status = AdminOpStatus.Unknown, Reason = err.Reason ?? "", Message = err.Message ?? "" };
        }



        // ============================================================
        // Старые методы (используются всем остальным UI — НЕ менялись)
        // ============================================================

        public static async Task<bool> RevokeSubmissionAsync(int assignmentId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"assignments/{assignmentId}/revoke"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> UpdateGradingPolicyAsync(int courseId, string policyJson)
        {
            EnsureBaseUrl();
            var payload = new { Policy = policyJson };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{courseId}/grading") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> DeleteStudentAsync(int id)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"users/{id}"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> DeleteGroupAsync(int id)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"groups/{id}"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> DeleteCourseAsync(int id)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"courses/{id}"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<List<StudentUser>> GetStudentsAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "users/students"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<StudentUser>>(json);
            }
            await HandleErrorAsync(response);
            return new List<StudentUser>();
        }

        public static async Task<bool> UpdateStudentAsync(int id, string login, string password, string fullName, int? groupId, int version)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName, GroupId = groupId, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"users/{id}") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> UpdateGroupAsync(int id, string name, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"groups/{id}") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> UpdateCourseAsync(int id, string name, string description, int? teacherId, int archived, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId, Archived = archived, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{id}") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> UpdateCourseMetaAsync(int id, string name, string description, int? teacherId, int archived, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId, Archived = archived, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{id}/meta") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<List<CourseStudent>> GetCourseStudentsAsync(int courseId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + $"courses/{courseId}/students"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<CourseStudent>>(json) ?? new List<CourseStudent>();
            }
            await HandleErrorAsync(response);
            return new List<CourseStudent>();
        }

        public static async Task<(bool Success, string Error)> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            EnsureBaseUrl();
            var payload = new { OldPassword = oldPassword, NewPassword = newPassword };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Auth/change-password") { Content = content });
            }
            if (response.IsSuccessStatusCode) return (true, "");
            string body = await response.Content.ReadAsStringAsync();
            try
            {
                var err = JsonConvert.DeserializeAnonymousType(body, new { Error = "" });
                return (false, err?.Error ?? "Ошибка сервера");
            }
            catch { return (false, "Ошибка сервера"); }
        }

        public static async Task<CurrentUserInfo> GetCurrentUserAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "Auth/me"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<CurrentUserInfo>(json);
            }
            await HandleErrorAsync(response);
            return null;
        }

        public static async Task<bool> UpdateMyProfileAsync(string fullName)
        {
            EnsureBaseUrl();
            var payload = new { FullName = fullName };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Put, BaseUrl + "Auth/profile") { Content = content });
            if (response.IsSuccessStatusCode && CurrentUser != null) CurrentUser.FullName = fullName;
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<List<StudentUser>> GetTeachersAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "Admin/teachers"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<StudentUser>>(json) ?? new List<StudentUser>();
            }
            await HandleErrorAsync(response);
            return new List<StudentUser>();
        }

        public static async Task<(bool Success, string Error)> CreateTeacherAsync(string login, string password, string fullName)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Admin/teachers") { Content = content });
            }
            if (response.IsSuccessStatusCode) return (true, "");
            // Извлекаем понятное сообщение через ParseError (он уже учитывает прикреплённый ApiError).
            var err = await ParseError(response);
            string msg = !string.IsNullOrEmpty(err?.Message) ? err.Message : "Ошибка сервера";
            return (false, msg);
        }

        public static async Task<bool> DeleteTeacherAsync(int id)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"Admin/teachers/{id}"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<(bool Success, string Error)> UpdateTeacherAsync(int id, string login, string fullName, int version)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, FullName = fullName, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"Admin/teachers/{id}") { Content = content });
            }
            if (response.IsSuccessStatusCode) return (true, "");
            var err = await ParseError(response);
            string msg = !string.IsNullOrEmpty(err?.Message) ? err.Message : "Ошибка сервера";
            return (false, msg);
        }

        public static async Task<(bool Success, string Error)> ResetUserPasswordAsync(int userId, string newPassword)
        {
            EnsureBaseUrl();
            var payload = new { NewPassword = newPassword };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"Admin/reset-password/{userId}") { Content = content });
            }
            if (response.IsSuccessStatusCode) return (true, "");
            var err = await ParseError(response);
            string msg = !string.IsNullOrEmpty(err?.Message) ? err.Message : "Ошибка сервера";
            return (false, msg);
        }

        public static async Task<List<Group>> GetGroupsAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "groups"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<Group>>(json);
            }
            await HandleErrorAsync(response);
            return new List<Group>();
        }

        public static async Task<bool> CreateGroupAsync(string name)
        {
            EnsureBaseUrl();
            var payload = new { Name = name };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "groups") { Content = content };
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(request);
            }
            if (response.IsSuccessStatusCode) return true;

            // Если сервер вернул "имя занято" — пробрасываем понятное сообщение через OnConflict,
            // потому что у этого старого метода нет другого способа сообщить причину вызывающему UI.
            int code = (int)response.StatusCode;
            if (code == 409 || code == 410)
            {
                var err = TryGetAttachedError(response) ?? await ParseError(response);
                if (IsDuplicateNameReason(err?.Reason))
                {
                    try { OnConflict?.Invoke(err); } catch { }
                    return false;
                }
            }
            await HandleErrorAsync(response);
            return false;
        }



        public static async Task<int> CreateCourseAsync(string name, string description, int? teacherId)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "courses") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeAnonymousType(json, new { Id = 0 });
                return result.Id;
            }
            await HandleErrorAsync(response);
            return -1;
        }

        public static async Task<List<int>> GetCourseGroupsAsync(int courseId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + $"courses/{courseId}/groups"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<int>>(json);
            }
            await HandleErrorAsync(response);
            return new List<int>();
        }

        public static async Task<bool> UpdateCourseGroupsAsync(int courseId, List<int> groupIds)
        {
            EnsureBaseUrl();
            var content = new StringContent(JsonConvert.SerializeObject(groupIds), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{courseId}/groups") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> CreateStudentAsync(string login, string password, string fullName, int? groupId)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName, GroupId = groupId, Role = "Student" };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "users") { Content = content };
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(request);
            }
            if (response.IsSuccessStatusCode) return true;

            int code = (int)response.StatusCode;
            if (code == 409 || code == 410)
            {
                var err = TryGetAttachedError(response) ?? await ParseError(response);
                if (IsDuplicateNameReason(err?.Reason))
                {
                    try { OnConflict?.Invoke(err); } catch { }
                    return false;
                }
            }
            await HandleErrorAsync(response);
            return false;
        }



        // ============================================================
        // НОВЫЕ admin-методы — возвращают AdminOpResult, БЕЗ автоматических окон.
        // Используются ИСКЛЮЧИТЕЛЬНО окнами Администрирования. Любые ошибки
        // (конфликт версии, удаление, валидация, сеть) UI обрабатывает сам
        // и показывает РОВНО ОДНО соответствующее сообщение.
        // ============================================================

        public static async Task<AdminOpResult> CreateGroupAdminAsync(string name)
        {
            EnsureBaseUrl();
            var payload = new { Name = name };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "groups") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateGroupAdminAsync(int id, string name, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"groups/{id}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> DeleteGroupAdminAsync(int id)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"groups/{id}");
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> CreateStudentAdminAsync(string login, string password, string fullName, int? groupId)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName, GroupId = groupId, Role = "Student" };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "users") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateStudentAdminAsync(int id, string login, string password, string fullName, int? groupId, int version)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName, GroupId = groupId, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"users/{id}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> DeleteStudentAdminAsync(int id)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"users/{id}");
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> CreateCourseAdminAsync(string name, string description, int? teacherId)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "courses") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateCourseAdminAsync(int id, string name, string description, int? teacherId, int archived, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId, Archived = archived, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{id}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateCourseMetaAdminAsync(int id, string name, string description, int? teacherId, int archived, int version)
        {
            EnsureBaseUrl();
            var payload = new { Name = name, Description = description, TeacherId = teacherId, Archived = archived, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{id}/meta") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateCourseGroupsAdminAsync(int courseId, List<int> groupIds)
        {
            EnsureBaseUrl();
            var content = new StringContent(JsonConvert.SerializeObject(groupIds), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"courses/{courseId}/groups") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> DeleteCourseAdminAsync(int id)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"courses/{id}");
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> CreateTeacherAdminAsync(string login, string password, string fullName)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, Password = password, FullName = fullName };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Admin/teachers") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateTeacherAdminAsync(int id, string login, string fullName, int version)
        {
            EnsureBaseUrl();
            var payload = new { Login = login, FullName = fullName, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"Admin/teachers/{id}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> DeleteTeacherAdminAsync(int id)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"Admin/teachers/{id}");
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> ResetUserPasswordAdminAsync(int userId, string newPassword)
        {
            EnsureBaseUrl();
            var payload = new { NewPassword = newPassword };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"Admin/reset-password/{userId}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }


        // НОВЫЕ admin-методы для ЗАДАНИЙ. Возвращают AdminOpResult, аналогично остальным
        // окнам администрирования. Это позволяет окну создания/редактирования задания
        // корректно отличать DuplicateName (название занято) от VersionConflict
        // (задание изменили) и AssignmentDeleted/CourseDeleted (удалено).
        public static async Task<AdminOpResult> CreateAssignmentAdminAsync(
            string title, string type, DateTime deadline, string status, int courseId, string description)
        {
            EnsureBaseUrl();
            var payload = new
            {
                Title = title,
                Type = type,
                Deadline = deadline,
                Status = status,
                CourseId = courseId,
                Description = description
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Assignments") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        public static async Task<AdminOpResult> UpdateAssignmentAdminAsync(
            int id, string title, string type, DateTime deadline, string status, string description, int version)
        {
            EnsureBaseUrl();
            var payload = new
            {
                Title = title,
                Type = type,
                Deadline = deadline,
                Status = status,
                Description = description,
                Version = version
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"assignments/{id}") { Content = content };
            return await ExecuteAdminRequestAsync(request);
        }

        // ============================================================
        // Все остальные методы (задания, решения, успеваемость) — без изменений.
        // ============================================================

        public static async Task<List<Assignment>> GetAssignmentsAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "Assignments/my"));
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<Assignment>>(json);
            }
            await HandleErrorAsync(response);
            return new List<Assignment>();
        }

        public static async Task<List<Submission>> GetSubmissionsAsync(int assignmentId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + $"Assignments/{assignmentId}/submissions"));
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<Submission>>(json);
            }
            await HandleErrorAsync(response);
            return new List<Submission>();
        }

        public static async Task<bool> UpdateSubmissionAsync(int submissionId, int? grade, string comment, string status, int version)
        {
            EnsureBaseUrl();
            var payload = new { Grade = grade, TeacherComment = comment, Status = status, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"Submissions/{submissionId}") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> SendCommentOnlyAsync(int submissionId, string comment, int version)
        {
            EnsureBaseUrl();
            var payload = new { TeacherComment = comment, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"Submissions/{submissionId}/comment") { Content = content };
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> StartCheckAsync(int submissionId)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"Submissions/{submissionId}/start-check");
            HttpResponseMessage response;
            using (SilentScope())
            {
                response = await SendWithSessionAsync(request);
            }
            return response.IsSuccessStatusCode;
        }

        public static async Task<bool> UnacceptSubmissionAsync(int submissionId)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"Submissions/{submissionId}/unaccept");
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<List<PerformanceRecord>> GetGlobalPerformanceAsync()
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "Performance/global");
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<PerformanceRecord>>(json);
            }
            await HandleErrorAsync(response);
            return new List<PerformanceRecord>();
        }

        public static async Task<List<StudentTaskRecord>> GetMyPerformanceAsync()
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "Performance/my");
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<StudentTaskRecord>>(json);
            }
            await HandleErrorAsync(response);
            return new List<StudentTaskRecord>();
        }

        public static async Task<bool> DeleteAssignmentAsync(int id)
        {
            EnsureBaseUrl();
            var request = new HttpRequestMessage(HttpMethod.Delete, BaseUrl + $"Assignments/{id}");
            var response = await SendWithSessionAsync(request);
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<List<Course>> GetMyCoursesAsync()
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "courses/my"));
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<Course>>(json);
            }
            await HandleErrorAsync(response);
            return new List<Course>();
        }

        public static async Task<HttpResponseMessage> CreateAssignmentAsync(string title, string type, DateTime deadline, string status, int courseId, string description)
        {
            EnsureBaseUrl();
            var payload = new { Title = title, Type = type, Deadline = deadline, Status = status, CourseId = courseId, Description = description };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "Assignments") { Content = content };
            using (SilentScope())
            {
                return await SendWithSessionAsync(request);
            }
        }

        public static async Task<HttpResponseMessage> UpdateAssignmentAsync(int id, string title, string type, DateTime deadline, string status, string description, int version)
        {
            EnsureBaseUrl();
            var payload = new { Title = title, Type = type, Deadline = deadline, Status = status, Description = description, Version = version };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + $"assignments/{id}") { Content = content };
            using (SilentScope())
            {
                return await SendWithSessionAsync(request);
            }
        }

        public static async Task<AssignmentState> GetAssignmentStateAsync(int assignmentId)
        {
            EnsureBaseUrl();
            try
            {
                HttpResponseMessage response;
                using (SilentScope())
                {
                    response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + $"assignments/{assignmentId}/state"));
                }
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
                    return JsonConvert.DeserializeObject<AssignmentState>(json);
                }
                if ((int)response.StatusCode == 410)
                {
                    return new AssignmentState { Id = assignmentId, IsDeleted = true };
                }
            }
            catch { }
            return null;
        }

        public static async Task<Submission> GetMySubmissionAsync(int assignmentId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + $"assignments/{assignmentId}/mysubmission"));
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json) || json == "null") return null;
                return JsonConvert.DeserializeObject<Submission>(json);
            }
            await HandleErrorAsync(response);
            return null;
        }

        public static async Task<bool> SaveDraftAsync(int assignmentId, string solutionJson, int? basedOnConfigVersion = null)
        {
            EnsureBaseUrl();
            object payload = basedOnConfigVersion.HasValue
                ? (object)new { SolutionJson = solutionJson, BasedOnConfigVersion = basedOnConfigVersion.Value }
                : new { SolutionJson = solutionJson };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"assignments/{assignmentId}/draft") { Content = content });
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> SubmitAssignmentAsync(int assignmentId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"assignments/{assignmentId}/submit"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }

        public static async Task<bool> ResetSubmissionAsync(int assignmentId)
        {
            EnsureBaseUrl();
            var response = await SendWithSessionAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + $"assignments/{assignmentId}/reset"));
            if (response.IsSuccessStatusCode) return true;
            await HandleErrorAsync(response);
            return false;
        }
    }
}
