using System.Text.RegularExpressions;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// Клиентские правила валидации. Полностью синхронизированы с серверной частью
    /// (Turing_Backend/Common/ValidationRules.cs). При изменении правил на сервере
    /// необходимо обновить этот файл.
    ///
    /// Все методы возвращают кортеж (bool Ok, string Error). Если Ok = true,
    /// значение Error не используется. Если Ok = false, Error содержит готовое
    /// сообщение для показа пользователю на русском языке.
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

        private static readonly Regex RxNamePart = new Regex(@"^[А-ЯЁ][а-яё]*(?:[\-' ][А-ЯЁа-яё]+)*$", RegexOptions.Compiled);
        private static readonly Regex RxLogin = new Regex(@"^[a-zA-Z][a-zA-Z0-9._\-]{2,31}$", RegexOptions.Compiled);
        private static readonly Regex RxPasswordAllowed = new Regex(@"^[A-Za-z0-9!@#$%^&*()_\-+=\[\]{};:,.?/\\|~`]+$", RegexOptions.Compiled);
        private static readonly Regex RxGroupName = new Regex(@"^[А-Яа-яЁёA-Za-z0-9][А-Яа-яЁёA-Za-z0-9 .\-]{0,30}[А-Яа-яЁёA-Za-z0-9.\-]$", RegexOptions.Compiled);
        private static readonly Regex RxCourseName = new Regex(@"^[А-Яа-яЁёA-Za-z0-9][А-Яа-яЁёA-Za-z0-9 .,\-:;()№""'+/!?&]*$", RegexOptions.Compiled);

        public static bool ValidateNamePart(string value, string fieldDisplayName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"Поле «{fieldDisplayName}» не может быть пустым.";
                return false;
            }
            string v = value.Trim();
            if (v.Length < FullNamePartMin || v.Length > FullNamePartMax)
            {
                error = $"«{fieldDisplayName}» должно содержать от {FullNamePartMin} до {FullNamePartMax} символов.";
                return false;
            }
            if (!RxNamePart.IsMatch(v))
            {
                error = $"«{fieldDisplayName}» может содержать только русские буквы (с возможными дефисом и апострофом). Первая буква должна быть заглавной. Например: «Иванов», «Анна-Мария», «Сергеевич».";
                return false;
            }
            return true;
        }

        public static bool ValidateFullNameParts(string lastName, string firstName, string middleName, bool noMiddleName, out string error)
        {
            if (!ValidateNamePart(lastName, "Фамилия", out error)) return false;
            if (!ValidateNamePart(firstName, "Имя", out error)) return false;
            if (!noMiddleName)
            {
                if (string.IsNullOrWhiteSpace(middleName))
                {
                    error = "Поле «Отчество» не может быть пустым. Если отчества нет, отметьте галочкой пункт «Без отчества».";
                    return false;
                }
                if (!ValidateNamePart(middleName, "Отчество", out error)) return false;
            }
            return true;
        }

        public static string CombineFullName(string lastName, string firstName, string middleName, bool noMiddleName)
        {
            string l = (lastName ?? "").Trim();
            string f = (firstName ?? "").Trim();
            string m = (middleName ?? "").Trim();
            if (noMiddleName || string.IsNullOrEmpty(m))
                return $"{l} {f}".Trim();
            return $"{l} {f} {m}".Trim();
        }

        /// <summary>
        /// Разбирает уже сохранённое ФИО на части. Если в ФИО только две части — отчество
        /// считается отсутствующим (noMiddleName = true).
        /// </summary>
        public static void SplitFullName(string fullName, out string lastName, out string firstName, out string middleName, out bool noMiddleName)
        {
            lastName = "";
            firstName = "";
            middleName = "";
            noMiddleName = true;
            if (string.IsNullOrWhiteSpace(fullName)) return;
            var parts = fullName.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1) lastName = parts[0];
            if (parts.Length >= 2) firstName = parts[1];
            if (parts.Length >= 3)
            {
                middleName = string.Join(" ", parts, 2, parts.Length - 2);
                noMiddleName = false;
            }
        }

        public static bool ValidateLogin(string login, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(login))
            {
                error = "Логин не может быть пустым.";
                return false;
            }
            string v = login.Trim();
            if (v.Length < LoginMin || v.Length > LoginMax)
            {
                error = $"Логин должен содержать от {LoginMin} до {LoginMax} символов.";
                return false;
            }
            if (v.Contains(" "))
            {
                error = "Логин не должен содержать пробелы.";
                return false;
            }
            if (!RxLogin.IsMatch(v))
            {
                error = "Логин должен начинаться с латинской буквы и может содержать только латинские буквы, цифры, точку, дефис и подчёркивание. Русские буквы и спецсимволы запрещены.";
                return false;
            }
            return true;
        }

        public static bool ValidatePassword(string password, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(password))
            {
                error = "Пароль не может быть пустым.";
                return false;
            }
            if (password.Length < PasswordMin)
            {
                error = $"Пароль должен содержать минимум {PasswordMin} символов.";
                return false;
            }
            if (password.Length > PasswordMax)
            {
                error = $"Пароль не может быть длиннее {PasswordMax} символов.";
                return false;
            }
            if (password.Contains(" "))
            {
                error = "Пароль не должен содержать пробелы.";
                return false;
            }
            if (!RxPasswordAllowed.IsMatch(password))
            {
                error = "Пароль может содержать только латинские буквы, цифры и стандартные спецсимволы (!@#$%^&*()-_=+[]{};:,.?/ и т.п.). Русские буквы запрещены.";
                return false;
            }
            bool hasLetter = false, hasDigit = false;
            foreach (var c in password)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) hasLetter = true;
                if (c >= '0' && c <= '9') hasDigit = true;
            }
            if (!hasLetter)
            {
                error = "Пароль должен содержать хотя бы одну латинскую букву.";
                return false;
            }
            if (!hasDigit)
            {
                error = "Пароль должен содержать хотя бы одну цифру.";
                return false;
            }
            return true;
        }

        public static bool ValidateGroupName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Название группы не может быть пустым.";
                return false;
            }
            string v = name.Trim();
            if (v.Length < GroupNameMin || v.Length > GroupNameMax)
            {
                error = $"Название группы должно содержать от {GroupNameMin} до {GroupNameMax} символов.";
                return false;
            }
            if (!RxGroupName.IsMatch(v))
            {
                error = "Название группы может содержать только русские/латинские буквы, цифры, дефис, точку и пробел. Должно начинаться и заканчиваться буквой или цифрой.";
                return false;
            }
            return true;
        }

        public static bool ValidateCourseName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Название курса не может быть пустым.";
                return false;
            }
            string v = name.Trim();
            if (v.Length < CourseNameMin || v.Length > CourseNameMax)
            {
                error = $"Название курса должно содержать от {CourseNameMin} до {CourseNameMax} символов.";
                return false;
            }
            if (v.Contains("\n") || v.Contains("\r") || v.Contains("\t"))
            {
                error = "Название курса не должно содержать переводы строк или табуляции.";
                return false;
            }
            if (!RxCourseName.IsMatch(v))
            {
                error = "Название курса должно начинаться с буквы или цифры и может содержать только буквы, цифры и базовую пунктуацию.";
                return false;
            }
            return true;
        }

        public static bool ValidateAssignmentTitle(string title, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(title))
            {
                error = "Название задания не может быть пустым.";
                return false;
            }
            string v = title.Trim();
            if (v.Length < AssignmentTitleMin || v.Length > AssignmentTitleMax)
            {
                error = $"Название задания должно содержать от {AssignmentTitleMin} до {AssignmentTitleMax} символов.";
                return false;
            }
            if (v.Contains("\n") || v.Contains("\r") || v.Contains("\t"))
            {
                error = "Название задания не должно содержать переводы строк или табуляции.";
                return false;
            }
            if (!RxCourseName.IsMatch(v))
            {
                error = "Название задания должно начинаться с буквы или цифры и может содержать только буквы, цифры и базовую пунктуацию.";
                return false;
            }
            return true;
        }
    }
}
