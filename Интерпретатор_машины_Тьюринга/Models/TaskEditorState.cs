using System;

namespace Интерпретатор_машины_Тьюринга
{
    public class TaskEditorState
    {
        public int? TaskId { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public DateTime Deadline { get; set; }
        public string Status { get; set; }
        public string ConfigurationJson { get; set; }
        public bool IsLocked { get; set; }
        public int Version { get; set; }
    }
}
