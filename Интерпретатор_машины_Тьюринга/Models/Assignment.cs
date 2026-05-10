using System;
using System.Collections.Generic;

namespace Интерпретатор_машины_Тьюринга
{
    public class Assignment
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public DateTime Deadline { get; set; }
        public string Status { get; set; }
        public string CourseName { get; set; }
        public string Description { get; set; }
        public string SubmissionStatus { get; set; }
        public List<Submission> Submissions { get; set; }
        public int CourseId { get; set; }
        public int IsLocked { get; set; }
        public int ConfigVersion { get; set; } = 1;
        public int Version { get; set; }
    }
}
