using System.Text.RegularExpressions;

namespace Turing_Backend.Common;

/// <summary>
/// Единые правила валидации для серверной и клиентской частей.
/// Правила синхронизированы: если что-то меняется здесь — должно меняться и в клиентском
/// ValidationRules.cs (на C# 7.3 / .NET Framework 4.8).
///
/// Принципы:
///   • ФИО: только русские буквы, дефис, пробел, апостроф; первая буква каждого
///     "слова" — заглавная; длина каждой части от 1 до 50 символов.
///   • Логин: латинские буквы, цифры, точка, дефис, подчёркивание; начинается с буквы;
///     длина 3–32; без пробелов; регистронезависимая уникальность не проверяется здесь
///     (это делает БД), но нормализуется к нижнему регистру.
///   • Пароль: минимум 6 символов, обязательно хотя бы одна буква и одна цифра;
///     допустимы латинские буквы, цифры и стандартные спецсимволы; без пробелов;
///     максимум 64 символа.
///   • Название группы: 2–32 символа, разрешены русские/латинские буквы, цифры,
///     дефис, точка, пробел; запрещены управляющие символы и крайние пробелы.
///   • Название курса: 2–100 символов, печатные русские/латинские буквы, цифры,
///     базовая пунктуация; без crlf и без обрамляющих пробелов.
///   • Название задания: те же правила, что и название курса (1–120 символов).
/// </summary>
public static class ValidationRules
{
    public const int FullNamePartMin = 1;
    public const int FullNamePartMax = 50;

    public const int LoginMin = 3;
    public const int LoginMax = 32;

    public const int PasswordMin = 6;
    public const int PasswordMax = 64;

    public const int GroupNameMin = 2;
    public const int GroupNameMax = 32;

    public const int CourseNameMin = 2;
    public const int CourseNameMax = 100;

    public const int AssignmentTitleMin = 2;
    public const int AssignmentTitleMax = 120;

    private static readonly Regex RxNamePart = new(@"^[А-ЯЁ][а-яё]*(?:[\-' ][А-ЯЁа-яё]+)*$", RegexOptions.Compiled);
    private static readonly Regex RxLogin = new(@"^[a-zA-Z][a-zA-Z0-9._\-]{2,31}$", RegexOptions.Compiled);
    private static readonly Regex RxPasswordAllowed = new(@"^[A-Za-z0-9!@#$%^&*()_\-+=\[\]{};:,.?/\\|~`]+$", RegexOptions.Compiled);
    private static readonly Regex RxGroupName = new(@"^[А-Яа-яЁёA-Za-z0-9][А-Яа-яЁёA-Za-z0-9 .\-]{0,30}[А-Яа-яЁёA-Za-z0-9.\-]$", RegexOptions.Compiled);
    private static readonly Regex RxCourseName = new(@"^[А-Яа-яЁёA-Za-z0-9][А-Яа-яЁёA-Za-z0-9 .,\-:;()№""'+/!?&]*$", RegexOptions.Compiled);

    public static (bool Ok, string? Error) ValidateNamePart(string value, string fieldDisplayName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, $"Поле «{fieldDisplayName}» не может быть пустым.");
        var v = value.Trim();
        if (v.Length < FullNamePartMin || v.Length > FullNamePartMax)
            return (false, $"«{fieldDisplayName}» должно содержать от {FullNamePartMin} до {FullNamePartMax} символов.");
        if (!RxNamePart.IsMatch(v))
            return (false, $"«{fieldDisplayName}» может содержать только русские буквы, дефис и апостроф. Первая буква должна быть заглавной (например: Иванов, Анна-Мария, О'Коннор-Петров недопустим — только русский алфавит).");
        return (true, null);
    }

    /// <summary>
    /// Валидирует ФИО, представленное одной строкой "Фамилия Имя [Отчество]".
    /// Используется на сервере, который получает FullName в виде единой строки.
    /// </summary>
    public static (bool Ok, string? Error) ValidateFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (false, "ФИО не может быть пустым.");
        var parts = fullName.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return (false, "ФИО должно содержать минимум фамилию и имя, разделённые пробелом.");
        if (parts.Length > 3)
            return (false, "ФИО не может содержать больше трёх частей (Фамилия, Имя, Отчество).");

        var (okL, errL) = ValidateNamePart(parts[0], "Фамилия");
        if (!okL) return (false, errL);
        var (okF, errF) = ValidateNamePart(parts[1], "Имя");
        if (!okF) return (false, errF);
        if (parts.Length == 3)
        {
            var (okM, errM) = ValidateNamePart(parts[2], "Отчество");
            if (!okM) return (false, errM);
        }
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateLogin(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return (false, "Логин не может быть пустым.");
        var v = login.Trim();
        if (v.Length < LoginMin || v.Length > LoginMax)
            return (false, $"Логин должен содержать от {LoginMin} до {LoginMax} символов.");
        if (v.Contains(' '))
            return (false, "Логин не должен содержать пробелы.");
        if (!RxLogin.IsMatch(v))
            return (false, "Логин должен начинаться с латинской буквы и может содержать только латинские буквы, цифры, точку, дефис и подчёркивание.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (false, "Пароль не может быть пустым.");
        if (password.Length < PasswordMin)
            return (false, $"Пароль должен содержать минимум {PasswordMin} символов.");
        if (password.Length > PasswordMax)
            return (false, $"Пароль не может быть длиннее {PasswordMax} символов.");
        if (password.Contains(' '))
            return (false, "Пароль не должен содержать пробелы.");
        if (!RxPasswordAllowed.IsMatch(password))
            return (false, "Пароль может содержать только латинские буквы, цифры и стандартные спецсимволы (!@#$%^&* и т.п.). Русские буквы запрещены.");
        bool hasLetter = false, hasDigit = false;
        foreach (var c in password)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) hasLetter = true;
            if (c >= '0' && c <= '9') hasDigit = true;
        }
        if (!hasLetter)
            return (false, "Пароль должен содержать хотя бы одну латинскую букву.");
        if (!hasDigit)
            return (false, "Пароль должен содержать хотя бы одну цифру.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Название группы не может быть пустым.");
        var v = name.Trim();
        if (v.Length < GroupNameMin || v.Length > GroupNameMax)
            return (false, $"Название группы должно содержать от {GroupNameMin} до {GroupNameMax} символов.");
        if (!RxGroupName.IsMatch(v))
            return (false, "Название группы может содержать только русские/латинские буквы, цифры, дефис, точку и пробел. Начинаться и заканчиваться должно буквой или цифрой.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateCourseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Название курса не может быть пустым.");
        var v = name.Trim();
        if (v.Length < CourseNameMin || v.Length > CourseNameMax)
            return (false, $"Название курса должно содержать от {CourseNameMin} до {CourseNameMax} символов.");
        if (v.Contains('\n') || v.Contains('\r') || v.Contains('\t'))
            return (false, "Название курса не должно содержать переводы строк или табуляции.");
        if (!RxCourseName.IsMatch(v))
            return (false, "Название курса должно начинаться с буквы или цифры и может содержать только буквы, цифры и базовую пунктуацию.");
        return (true, null);
    }

    public static (bool Ok, string? Error) ValidateAssignmentTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (false, "Название задания не может быть пустым.");
        var v = title.Trim();
        if (v.Length < AssignmentTitleMin || v.Length > AssignmentTitleMax)
            return (false, $"Название задания должно содержать от {AssignmentTitleMin} до {AssignmentTitleMax} символов.");
        if (v.Contains('\n') || v.Contains('\r') || v.Contains('\t'))
            return (false, "Название задания не должно содержать переводы строк или табуляции.");
        if (!RxCourseName.IsMatch(v))
            return (false, "Название задания должно начинаться с буквы или цифры и может содержать только буквы, цифры и базовую пунктуацию.");
        return (true, null);
    }

    public static string NormalizeFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        var parts = fullName.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    public static string NormalizeLogin(string login) => (login ?? "").Trim();
}
