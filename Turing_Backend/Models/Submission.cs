namespace Turing_Backend.Models;

public class Submission
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public int StudentId { get; set; }
    public string SolutionJson { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public int? Grade { get; set; }
    public string? TeacherComment { get; set; }

    // Новая бизнес-логика: "Не сдано" / "Не оценено" / "Оценено".
    public string Status { get; set; } = "Не оценено";

    public int Version { get; set; }
    public int IsBeingChecked { get; set; }
    public DateTime? CheckStartedAt { get; set; }
    public int AssignmentConfigVersion { get; set; } = 1;
    public int IsOutdated { get; set; } = 0;

    // Аудит-поля.
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
