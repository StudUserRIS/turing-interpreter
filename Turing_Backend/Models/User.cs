namespace Turing_Backend.Models;

public class User
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public int? GroupId { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int MustChangePassword { get; set; }
    public int Version { get; set; }

    // Аудит-поля. Заполняются БД через DEFAULT NOW(); присутствуют в модели,
    // чтобы Dapper корректно гидратировал их при SELECT *.
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
