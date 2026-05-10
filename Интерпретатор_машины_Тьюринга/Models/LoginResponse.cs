namespace Интерпретатор_машины_Тьюринга
{
    public class LoginResponse
    {
        public string SessionId { get; set; }
        public string Role { get; set; }
        public string FullName { get; set; }
        public string Login { get; set; }
        public string Group { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
