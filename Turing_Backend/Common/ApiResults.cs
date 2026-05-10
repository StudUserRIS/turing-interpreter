using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turing_Backend.Common;

/// <summary>
/// Унифицированные ответы для конфликтных/ошибочных ситуаций.
///
/// КРИТИЧЕСКИ ВАЖНО: в .NET 8 Results.Json по умолчанию применяет CamelCase-политику
/// именования (JsonNamingPolicy.CamelCase) для свойств анонимных объектов, из-за чего
/// в теле ответа поля выходят как "reason"/"message"/"currentData", а не как
/// "Reason"/"Message"/"CurrentData". Клиент же исторически читает их именно
/// в PascalCase (parsed?["Reason"], parsed?["Message"]). При несовпадении регистра
/// клиент получает пустой Reason — и трактует ЛЮБУЮ 409-ошибку как VersionConflict,
/// показывая окно «Данные окна не являются актуальными» вместо правильного
/// «Логин/группа/курс/задание уже существует».
///
/// Чтобы исправить это раз и навсегда — формируем JSON через ЯВНЫЙ JsonSerializerOptions
/// с PropertyNamingPolicy = null. Тогда поля выходят строго как Reason/Message/CurrentData,
/// и клиент гарантированно видит Reason="DuplicateName".
/// </summary>
public static class ApiResults
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IResult Conflict(string reason, string message, object? currentData = null)
    {
        var payload = new ErrorPayload
        {
            Reason = reason ?? "",
            Message = message ?? "",
            CurrentData = currentData
        };
        return Results.Json(payload, JsonOpts, statusCode: 409);
    }

    public static IResult Gone(string reason, string message)
    {
        var payload = new ErrorPayload
        {
            Reason = reason ?? "",
            Message = message ?? "",
            CurrentData = null
        };
        return Results.Json(payload, JsonOpts, statusCode: 410);
    }

    public static IResult SessionEnded(string reason, string message)
    {
        var payload = new ErrorPayload
        {
            Reason = reason ?? "",
            Message = message ?? "",
            CurrentData = null
        };
        return Results.Json(payload, JsonOpts, statusCode: 401);
    }

    /// <summary>
    /// Явный DTO с PascalCase-именами свойств. Использование DTO вместо анонимного
    /// объекта гарантирует стабильный формат сериализации даже если в будущем
    /// изменится глобальная политика JSON в Program.cs.
    /// </summary>
    private sealed class ErrorPayload
    {
        public string Reason { get; set; } = "";
        public string Message { get; set; } = "";
        public object? CurrentData { get; set; }
    }
}
