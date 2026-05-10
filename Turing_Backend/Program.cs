using Turing_Backend.Database;
using Turing_Backend.Endpoints;
using Turing_Backend.Hubs;
using Turing_Backend.Services;
using Dapper;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ===================== Сетевая конфигурация =====================
var serverUrls = builder.Configuration["Server:Urls"] ?? "http://0.0.0.0:5007";
bool useHttps = builder.Configuration.GetValue<bool>("Server:UseHttps");
string? httpsCertPath = builder.Configuration["Server:HttpsCertificatePath"];
string? httpsCertPassword = builder.Configuration["Server:HttpsCertificatePassword"];

builder.WebHost.ConfigureKestrel(options =>
{
    foreach (var url in serverUrls.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var trimmed = url.Trim();
        var uri = new Uri(trimmed);
        if (uri.Scheme == "https" && useHttps && !string.IsNullOrEmpty(httpsCertPath) && File.Exists(httpsCertPath))
        {
            var ip = uri.Host == "0.0.0.0" || uri.Host == "+" || uri.Host == "*"
                ? System.Net.IPAddress.Any
                : System.Net.IPAddress.Parse(uri.Host);
            options.Listen(ip, uri.Port, listenOptions =>
            {
                listenOptions.UseHttps(httpsCertPath!, httpsCertPassword);
            });
        }
        else
        {
            var ip = uri.Host == "0.0.0.0" || uri.Host == "+" || uri.Host == "*"
                ? System.Net.IPAddress.Any
                : System.Net.IPAddress.Parse(uri.Host);
            options.Listen(ip, uri.Port);
        }
    }
});

// ===================== Сервисы =====================
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<NotificationService>();

// Фоновый сервис автоочистки устаревших технических записей в БД (LoginAttempts,
// SessionTerminationReasons). Минимизация хранения данных (152-ФЗ ст. 5 ч. 7).
builder.Services.AddHostedService<DataRetentionService>();

// ===================== CORS =====================
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });
}

// ===================== SignalR =====================
int keepAliveSec = builder.Configuration.GetValue<int?>("SignalR:KeepAliveSeconds") ?? 30;
int clientTimeoutSec = builder.Configuration.GetValue<int?>("SignalR:ClientTimeoutSeconds") ?? 120;
int handshakeTimeoutSec = builder.Configuration.GetValue<int?>("SignalR:HandshakeTimeoutSeconds") ?? 30;

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveSec);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(clientTimeoutSec);
    options.HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSec);
}).AddNewtonsoftJsonProtocol();

var app = builder.Build();

// ===================== Инициализация БД =====================
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
    DatabaseInitializer.Initialize(factory);
}

// ===================== Pipeline =====================
app.UseRouting();

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseMiddleware<SessionAuthMiddleware>();

app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapAssignmentEndpoints();
app.MapPerformanceEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

// ===================== Фоновая очистка =====================
//
// ВАЖНО: автоматическое завершение сессии по бездействию пользователя или по отсутствию
// SignalR-активности ПОЛНОСТЬЮ ОТКЛЮЧЕНО. Сессия живёт до тех пор, пока пользователь
// явно не выйдет (REST /Auth/logout или SignalR ClientLogout) или пока администратор
// не завершит её принудительно (смена логина/пароля, удаление учётной записи, вход с
// другого устройства).
//
// Раньше здесь работал orphan-cleanup таймер, который удалял сессии при разрыве
// SignalR-канала. На практике это приводило к тому, что свёрнутая программа, экран
// блокировки Windows, переход в режим энергосбережения или временный обрыв сети
// выкидывали пользователя из системы. По требованию заказчика этот функционал убран.
//
// Остаётся только техническая разблокировка "зависших" проверок работ — это НЕ
// связано с жизненным циклом сессии и относится только к флагу IsBeingChecked
// у конкретного Submission.

// "Зависшие" проверки работ (преподаватель ушёл, не оценив).
// Если работа была взята в проверку (IsBeingChecked=1), но преподаватель не сохранил
// результат в течение 10 минут — автоматически снимаем флаг, чтобы студент мог
// отозвать работу.
var checkCleanupTimer = new System.Threading.Timer(async _ =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
        using var db = factory.Create();
        await db.ExecuteAsync(
            "UPDATE Submissions SET IsBeingChecked = 0, CheckStartedAt = NULL " +
            "WHERE IsBeingChecked = 1 AND CheckStartedAt IS NOT NULL AND CheckStartedAt < @Cutoff",
            new { Cutoff = DateTime.Now.AddMinutes(-10) });
    }
    catch { }
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

Console.WriteLine($"[Turing_Backend] Server started, listening on: {serverUrls}");
app.Run();
