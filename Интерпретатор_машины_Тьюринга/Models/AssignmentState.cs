using System;

namespace Интерпретатор_машины_Тьюринга
{
    // Класс для проверки актуального состояния задания на сервере.
    // Используется студентом, чтобы перед действием убедиться, что задание не было
    // удалено / скрыто / переведено в черновик / архивировано преподавателем.
    public class AssignmentState
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public DateTime Deadline { get; set; }
        public string Status { get; set; }
        public string Description { get; set; }
        public int Version { get; set; }
        public int CourseId { get; set; }
        public string CourseName { get; set; }
        public int CourseArchived { get; set; }
        public bool IsDeleted { get; set; }
    }
}
