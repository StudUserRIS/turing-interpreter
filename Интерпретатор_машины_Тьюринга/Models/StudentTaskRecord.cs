using System;

namespace Интерпретатор_машины_Тьюринга
{
    public class StudentTaskRecord
    {
        public string Course { get; set; }
        public string Task { get; set; }
        public string Type { get; set; }
        public DateTime Deadline { get; set; }
        public int? Grade { get; set; }
        public string Status { get; set; }
        public string GradingPolicy { get; set; }
    }
}
