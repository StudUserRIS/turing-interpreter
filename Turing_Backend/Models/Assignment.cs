namespace Turing_Backend.Models;

public class Assignment
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "Домашняя работа";
    public DateTime Deadline { get; set; }

    // Новая бизнес-логика: только "Опубликовано" / "Архив".
    // DEFAULT в БД теперь "Архив" (вместо мусорного '').
    public string Status { get; set; } = "Архив";

    public int CourseId { get; set; }
    public string? Description { get; set; }
    public int IsLocked { get; set; }
    public int ConfigVersion { get; set; } = 1;
    public string? CourseName { get; set; }
    public string? SubmissionStatus { get; set; }
    public int Version { get; set; }

    // Аудит-поля.
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
