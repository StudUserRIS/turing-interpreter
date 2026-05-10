using System;

namespace Интерпретатор_машины_Тьюринга
{
    // Вспомогательный класс для возврата статуса сессии пользователя
    internal class UserSessionStatusInfo
    {
        public bool IsOnline { get; set; }
        public DateTime LastActivity { get; set; }
        public string IpAddress { get; set; } = "";
    }
}
