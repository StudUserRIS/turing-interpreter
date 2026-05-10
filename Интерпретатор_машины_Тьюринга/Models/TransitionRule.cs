namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// Доменный value-объект «правило перехода» машины Тьюринга.
    ///
    /// Класс объявлен как <c>public</c>, потому что используется в подписях
    /// публичных членов доменного класса <see cref="Core.TuringMachine"/>
    /// (свойство <c>RuleProvider</c>, метод <c>ParseRule</c>) и публичного
    /// результата шага <see cref="Core.StepResult"/> (свойство
    /// <c>AppliedRule</c>, фабричный метод <c>Executed</c>).
    ///
    /// Объявление <c>internal</c> приводило к ошибкам компилятора
    /// «Несогласованность по доступности», поскольку C# запрещает
    /// ссылаться на <c>internal</c>-тип из подписей <c>public</c>-членов.
    /// </summary>
    public class TransitionRule
    {
        /// <summary>Символ, который записывается на ленту вместо текущего.</summary>
        public char NewSymbol { get; set; }

        /// <summary>Направление сдвига головки: '→', '←' или '•' (на месте).</summary>
        public char Direction { get; set; }

        /// <summary>Имя нового состояния, в которое переходит машина.</summary>
        public string NewState { get; set; }
    }
}
