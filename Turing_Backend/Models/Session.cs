namespace Turing_Backend.Models;

public class Session
{
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
