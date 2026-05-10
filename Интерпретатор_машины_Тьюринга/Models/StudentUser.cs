using System;

namespace Интерпретатор_машины_Тьюринга
{
    public class StudentUser
    {
        public int Id { get; set; }
        public string Login { get; set; }
        public string FullName { get; set; }
        public int? GroupId { get; set; }
        public string Role { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int Version { get; set; }
    }
}
