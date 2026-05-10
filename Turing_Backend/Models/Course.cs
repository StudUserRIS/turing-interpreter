namespace Turing_Backend.Models;

public class Course
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int TeacherId { get; set; }
    public string? GradingPolicy { get; set; }
    public string? Description { get; set; }
    public int Archived { get; set; }
    public string? TeacherName { get; set; } // для отображения в UI
    public int Version { get; set; }

    // Аудит-поля.
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
