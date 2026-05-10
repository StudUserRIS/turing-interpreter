using System.Collections.Generic;

namespace Интерпретатор_машины_Тьюринга
{
    internal class ExecutionState
    {
        public string CurrentState { get; set; }
        public int HeadPosition { get; set; }
        public Dictionary<int, char> Tape { get; set; }
        public int Steps { get; set; }
    }
}
