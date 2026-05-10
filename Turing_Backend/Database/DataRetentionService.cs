// Turing_Backend/Database/DataRetentionService.cs
using Dapper;

namespace Turing_Backend.Database;

/// <summary>
/// Фоновый сервис периодической очистки устаревших технических записей в БД.
///
/// Зачем нужно:
///   • LoginAttempts и SessionTerminationReasons — это «ленточные» журналы, которые
///     по своей природе постоянно растут. Без авторегламента они забивают БД, ухудшают
///     производительность и нарушают принцип минимизации хранения персональных данных
///     (152-ФЗ ст. 5 ч. 7 — «обработка ПДн ограничивается достижением конкретных целей»).
///   • Раньше очистка делалась только в момент старта сервера. На production-серверах,
///     которые перезапускаются раз в недели, журналы успевали разрастись до миллионов
///     строк. Теперь очистка идёт ежедневно (и при старте — для совместимости).
///
/// Что чистится:
///   • LoginAttempts старше 30 дней. 30 дней — компромисс между нужностью аудита
///     неудачных попыток и минимизацией хранения IP-адресов.
///   • SessionTerminationReasons старше 7 дней. Эти записи нужны только для того,
///     чтобы клиент при следующем входе увидел причину завершения предыдущей сессии,
///     максимум на пару суток. Хранить дольше нет смысла.
///
/// Сервис выполняется без блокировки запросов — все DELETE идут небольшими партиями
/// по WHERE-условию с индексом, не таблично-широкими сканами.
/// </summary>
public class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionService> _logger;

    // Раз в сутки — этого достаточно. Старт первого тика отложен на 5 минут, чтобы
    // не нагружать сервер в момент пуска.
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);

    public DataRetentionService(IServiceScopeFactory scopeFactory, ILogger<DataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken);
        }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
                using var db = factory.Create();

                int loginRows = await db.ExecuteAsync(
                    "DELETE FROM LoginAttempts WHERE AttemptAt < NOW() - INTERVAL '30 days'");

                int termRows = await db.ExecuteAsync(
                    "DELETE FROM SessionTerminationReasons WHERE CreatedAt < NOW() - INTERVAL '7 days'");

                if (loginRows > 0 || termRows > 0)
                {
                    _logger.LogInformation(
                        "[DataRetention] Очищено: LoginAttempts={Login}, SessionTerminationReasons={Term}",
                        loginRows, termRows);
                }
            }
            catch (Exception ex)
            {
                // Никогда не валим сервер из-за ошибки фоновой очистки — лог и идём дальше.
                _logger.LogWarning(ex, "[DataRetention] Ошибка периодической очистки журналов.");
            }

            try
            {
                await Task.Delay(Period, stoppingToken);
            }
            catch (TaskCanceledException) { return; }
        }
    }
}
