namespace Turing_Backend.Models;

public class StudentTaskRecord
{
    public string? Course { get; set; }
    public string? Task { get; set; }
    public string? Type { get; set; }
    public DateTime Deadline { get; set; }
    public int? Grade { get; set; }
    public string? Status { get; set; }
    public string? GradingPolicy { get; set; }
}
