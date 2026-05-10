using Npgsql;

namespace Turing_Backend.Common;

/// <summary>
/// Помощник для распознавания типичных ошибок PostgreSQL, которые могут возникнуть
/// в результате гонки между двумя одновременными запросами.
///
/// После того как в БД были введены реальные UNIQUE-индексы (ux_users_login_ci,
/// ux_groups_name_ci, ux_courses_name_ci, ux_assignments_course_title_ci,
/// ux_submissions_assignment_student, ux_sessions_userid), любая параллельная
/// вставка может вернуть PostgresException с SqlState = "23505" (unique_violation).
/// Раньше это превращалось в HTTP 500 «Внутренняя ошибка сервера»; теперь
/// эндпоинты могут обработать такую ситуацию и вернуть пользователю понятный
/// 409 Conflict с Reason="DuplicateName" — точно так же, как и при «штатной»
/// проверке через SELECT.
///
/// Это закрывает последний пробел между «целостность гарантирована БД» и
/// «пользователь видит понятное сообщение, а не 500».
/// </summary>
public static class PostgresErrorHelper
{
    /// <summary>
    /// PostgreSQL SQLSTATE 23505 — нарушение уникальности.
    /// </summary>
    public const string UniqueViolation = "23505";

    /// <summary>
    /// PostgreSQL SQLSTATE 23503 — нарушение FK.
    /// </summary>
    public const string ForeignKeyViolation = "23503";

    /// <summary>
    /// PostgreSQL SQLSTATE 23514 — нарушение CHECK-ограничения.
    /// </summary>
    public const string CheckViolation = "23514";

    /// <summary>
    /// Возвращает true, если исключение — это нарушение уникального индекса.
    /// Безопасно к null и к произвольным обёрткам исключений.
    /// </summary>
    public static bool IsUniqueViolation(Exception? ex)
    {
        return GetSqlState(ex) == UniqueViolation;
    }

    /// <summary>
    /// Возвращает true, если исключение — это нарушение CHECK-ограничения.
    /// Используется, например, при попытке записать недопустимый Status или Role.
    /// </summary>
    public static bool IsCheckViolation(Exception? ex)
    {
        return GetSqlState(ex) == CheckViolation;
    }

    /// <summary>
    /// Возвращает true, если исключение — это нарушение FK.
    /// </summary>
    public static bool IsForeignKeyViolation(Exception? ex)
    {
        return GetSqlState(ex) == ForeignKeyViolation;
    }

    /// <summary>
    /// Достаёт SqlState из PostgresException, перебирая InnerException-цепочку.
    /// </summary>
    public static string? GetSqlState(Exception? ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is PostgresException pg)
                return pg.SqlState;
            current = current.InnerException;
        }
        return null;
    }

    /// <summary>
    /// Распознаёт по тексту имени ограничения, какое именно «занятое имя» вызвало конфликт.
    /// Это используется, когда эндпоинт хочет дать более конкретное сообщение
    /// (например, «Логин уже занят» против «Группа с таким названием уже существует»).
    /// </summary>
    public static string? GetConstraintName(Exception? ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is PostgresException pg)
                return pg.ConstraintName;
            current = current.InnerException;
        }
        return null;
    }
}
