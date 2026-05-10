using System.Drawing;

namespace Интерпретатор_машины_Тьюринга
{
    public class TapeCell
    {
        public char Value { get; set; }
        public bool IsHead { get; set; }
        public RectangleF Bounds { get; set; }
        public TapeCell(char value)
        {
            Value = value;
        }
    }
}
