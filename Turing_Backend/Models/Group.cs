namespace Turing_Backend.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }

    // Аудит-поля.
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
