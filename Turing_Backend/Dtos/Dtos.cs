namespace Turing_Backend.Dtos;

public class LoginRequest
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
}

public class CreateGroupDto
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
}

public class UpdateGroupDto
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
}

public class CreateCourseDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? TeacherId { get; set; }
}

public class UpdateCourseDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? TeacherId { get; set; }
    public int Archived { get; set; }
    public int Version { get; set; }
}

public class CreateUserDto
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "Student";
    public int? GroupId { get; set; }
}

public class UpdateUserDto
{
    public string Login { get; set; } = "";
    public string? Password { get; set; }
    public string FullName { get; set; } = "";
    public int? GroupId { get; set; }
    public int Version { get; set; }
}

public class UpdateGradingDto
{
    public string Policy { get; set; } = "";
}

public class CreateAssignmentDto
{
    public string Title { get; set; } = "";
    public string Type { get; set; } = "Домашняя работа";
    public DateTime Deadline { get; set; }

    // Новая бизнес-логика: статусы "Опубликовано" / "Архив". Старое значение
    // "Черновик" больше не выставляется по умолчанию — но оставшиеся клиенты,
    // которые присылают "Черновик"/"Скрыто", сервер принудительно нормализует в "Архив".
    public string Status { get; set; } = "Архив";

    public int CourseId { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; }
}

public class GradeSubmissionDto
{
    public int? Grade { get; set; }
    public string? TeacherComment { get; set; }
    public string Status { get; set; } = "Оценено";
    public int Version { get; set; }
}

public class CommentOnlySubmissionDto
{
    public string? TeacherComment { get; set; }
    public int Version { get; set; }
}

public class SubmitSubmissionDto
{
    public int AssignmentId { get; set; }
    public string SolutionJson { get; set; } = "";

    // Версия конфигурации задания (Assignment.ConfigVersion), на основе которой
    // подготовлено сохраняемое решение. Заполняется клиентом, когда он уверен,
    // что работает с актуальной МТ (только что открыл задание с актуальной МТ
    // или сбросил решение к исходной конфигурации преподавателя). Если значение
    // совпадает с текущей ConfigVersion задания — сервер снимает флаг IsOutdated
    // и обновляет AssignmentConfigVersion. Если значение не передано (null) или
    // не совпадает — флаг и привязка к версии остаются прежними.
    public int? BasedOnConfigVersion { get; set; }
}

public class ChangePasswordDto
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

// DTO оставлен для обратной совместимости со старыми клиентами, которые ещё могут
// отправлять PUT /api/Auth/profile. Сам эндпоинт возвращает 403 — изменение ФИО
// доступно только администратору.
public class UpdateProfileDto
{
    public string FullName { get; set; } = "";
}

public class CreateTeacherDto
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FullName { get; set; } = "";
}

public class ResetPasswordDto
{
    public string NewPassword { get; set; } = "";
}

public class UpdateTeacherDto
{
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public int Version { get; set; }
}

public class ConflictResponse
{
    public string Reason { get; set; } = "VersionConflict";
    public string Message { get; set; } = "";
    public object? CurrentData { get; set; }
}
