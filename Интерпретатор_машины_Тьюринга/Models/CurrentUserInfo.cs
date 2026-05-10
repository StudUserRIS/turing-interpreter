namespace Интерпретатор_машины_Тьюринга
{
    // Класс для эндпоинта /api/Auth/me — содержит актуальные данные пользователя из БД.
    // Используется heartbeat-таймером для обнаружения изменений профиля администратором.
    public class CurrentUserInfo
    {
        public int Id { get; set; }
        public string Login { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Group { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
