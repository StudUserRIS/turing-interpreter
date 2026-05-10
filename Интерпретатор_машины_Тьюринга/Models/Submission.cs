using System;

namespace Интерпретатор_машины_Тьюринга
{
    public class Submission
    {
        public int Id { get; set; }
        public string SolutionJson { get; set; }
        public DateTime SubmittedAt { get; set; }
        public int? Grade { get; set; }
        public string TeacherComment { get; set; }
        public string Status { get; set; }
        public string StudentName { get; set; }
        public string GroupName { get; set; }
        public int Version { get; set; }
        public int IsBeingChecked { get; set; }
        public DateTime? CheckStartedAt { get; set; }
        public int AssignmentConfigVersion { get; set; } = 1;
        public int IsOutdated { get; set; } = 0;
    }
}
