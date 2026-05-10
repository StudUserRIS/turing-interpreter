namespace Интерпретатор_машины_Тьюринга
{
    public class Course
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int TeacherId { get; set; }
        public string GradingPolicy { get; set; }
        public string Description { get; set; }
        public int Archived { get; set; }
        public string TeacherName { get; set; }
        public int Version { get; set; }
        public override string ToString() => Name;
    }
}
