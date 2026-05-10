using System;
using System.Collections.Generic;

namespace Интерпретатор_машины_Тьюринга
{
    [Serializable]
    public class TuringMachineData
    {
        public string Alphabet { get; set; }
        public Dictionary<int, char> TapeContent { get; set; }
        public int HeadPosition { get; set; }
        public List<TuringState> States { get; set; }
        public Dictionary<string, Dictionary<string, string>> TransitionRules { get; set; }
        public string ProblemCondition { get; set; }
        public string Comment { get; set; }
    }
}
