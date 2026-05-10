using System;
using System.Collections.Generic;
using System.Linq;

namespace Интерпретатор_машины_Тьюринга.Core
{
    /// <summary>
    /// Доменная модель «Машина Тьюринга».
    ///
    /// SRP: класс отвечает ИСКЛЮЧИТЕЛЬНО за бизнес-логику абстрактной машины —
    /// пошаговое выполнение, поиск правила перехода, проверку конечного состояния,
    /// обнаружение зацикливания. Класс полностью независим от WinForms,
    /// DataGridView и любых UI-элементов.
    ///
    /// UI-слой (Form1.ExecutionEngine.cs) выступает координатором: он передаёт
    /// в машину провайдеры данных (текущий символ ленты, набор правил)
    /// и реагирует на результат шага. Это устраняет смешение уровней
    /// абстракции, на которое указывает разбор архитектуры.
    /// </summary>
    public sealed class TuringMachine
    {
        // ──────────────────────────────────────────────────────────
        // Внутреннее состояние выполнения
        // ──────────────────────────────────────────────────────────
        private string _currentState;
        private int _headPosition;
        private int _steps;

        public string CurrentState => _currentState;
        public int HeadPosition => _headPosition;
        public int Steps => _steps;
        public bool IsRunning { get; private set; }

        // ──────────────────────────────────────────────────────────
        // Делегаты-провайдеры (инверсия зависимостей).
        // Машина не знает, откуда берутся правила и где живёт лента —
        // это конкретная задача UI-слоя.
        // ──────────────────────────────────────────────────────────
        public Func<string, char, TransitionRule> RuleProvider { get; set; }
        public Func<char> CurrentSymbolProvider { get; set; }
        public Action<char> CurrentSymbolWriter { get; set; }
        public Action MoveHeadLeftAction { get; set; }
        public Action MoveHeadRightAction { get; set; }

        // ──────────────────────────────────────────────────────────
        // Жизненный цикл
        // ──────────────────────────────────────────────────────────
        public void Reset(string initialState, int initialHeadPosition)
        {
            _currentState = initialState;
            _headPosition = initialHeadPosition;
            _steps = 0;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        // ──────────────────────────────────────────────────────────
        // Выполнение одного шага.
        // Возвращает результат с описанием произошедшего, чтобы UI мог
        // решать, что делать дальше: показать диалог, обновить ленту,
        // прервать таймер и т. д.
        // ──────────────────────────────────────────────────────────
        public StepResult Step()
        {
            if (!IsRunning)
                return StepResult.Stopped("Машина не запущена.");

            char currentSymbol = CurrentSymbolProvider();
            int beforePosition = _headPosition;
            string beforeState = _currentState;

            var rule = RuleProvider(_currentState, currentSymbol);
            if (rule == null)
            {
                IsRunning = false;
                char displayed = currentSymbol == ' ' ? '_' : currentSymbol;
                return StepResult.NoRule(beforeState, displayed, beforePosition, _steps);
            }

            char symbolToWrite = rule.NewSymbol == '_' ? ' ' : rule.NewSymbol;
            CurrentSymbolWriter(symbolToWrite);

            if (rule.Direction == '→') { MoveHeadRightAction(); _headPosition++; }
            else if (rule.Direction == '←') { MoveHeadLeftAction(); _headPosition--; }

            _currentState = rule.NewState;
            _steps++;

            bool isFinal = IsFinalState(_currentState);
            if (isFinal) IsRunning = false;

            return StepResult.Executed(
                beforeState, beforePosition, currentSymbol, rule, _currentState, _steps, isFinal);
        }

        // ──────────────────────────────────────────────────────────
        // Полный прогон (мгновенное выполнение) с защитой от зацикливания.
        // Возвращает итог: завершено / зациклено / нет правила / лимит шагов.
        // ──────────────────────────────────────────────────────────
        public InstantRunResult RunInstant(int maxSteps, Action<StepResult> onStep)
        {
            if (!IsRunning)
                return new InstantRunResult { Outcome = InstantRunOutcome.NotStarted, Steps = _steps };

            var visited = new Dictionary<(string, char, int), int>();
            while (_steps < maxSteps)
            {
                var key = (_currentState, CurrentSymbolProvider(), _headPosition);
                if (visited.ContainsKey(key))
                {
                    if (visited[key] > 2)
                        return new InstantRunResult { Outcome = InstantRunOutcome.Looped, Steps = _steps };
                    visited[key]++;
                }
                else visited[key] = 1;

                var result = Step();
                onStep?.Invoke(result);

                if (result.Kind == StepKind.NoRule)
                    return new InstantRunResult { Outcome = InstantRunOutcome.NoRule, Steps = _steps };
                if (result.IsFinal)
                    return new InstantRunResult { Outcome = InstantRunOutcome.Completed, Steps = _steps };
            }
            return new InstantRunResult { Outcome = InstantRunOutcome.MaxStepsReached, Steps = _steps };
        }

        // ──────────────────────────────────────────────────────────
        // Чистые функции домена
        // ──────────────────────────────────────────────────────────
        public static bool IsFinalState(string stateName)
        {
            return stateName == "qF" || (stateName != null && stateName.EndsWith("!"));
        }

        public static TransitionRule ParseRule(string ruleText, string fallbackState)
        {
            if (string.IsNullOrEmpty(ruleText) || ruleText.Length < 2) return null;
            var rule = new TransitionRule
            {
                NewSymbol = ruleText[0],
                Direction = ruleText[1] switch
                {
                    '→' => '→',
                    '>' => '→',
                    '←' => '←',
                    '<' => '←',
                    '•' => '•',
                    '.' => '•',
                    _   => '•'
                },
                NewState = ruleText.Length > 2 ? ruleText.Substring(2).Trim() : fallbackState
            };
            return rule;
        }
    }

    // ──────────────────────────────────────────────────────────
    // Доменные value-объекты результатов
    // ──────────────────────────────────────────────────────────
    public enum StepKind { Executed, NoRule, Stopped }

    public sealed class StepResult
    {
        public StepKind Kind { get; private set; }
        public string Message { get; private set; }
        public string StateBefore { get; private set; }
        public int PositionBefore { get; private set; }
        public char SymbolBefore { get; private set; }
        public TransitionRule AppliedRule { get; private set; }
        public string StateAfter { get; private set; }
        public int Steps { get; private set; }
        public bool IsFinal { get; private set; }

        public static StepResult Executed(string stateBefore, int posBefore, char sym, TransitionRule rule,
                                          string stateAfter, int steps, bool isFinal) =>
            new StepResult
            {
                Kind = StepKind.Executed,
                StateBefore = stateBefore,
                PositionBefore = posBefore,
                SymbolBefore = sym,
                AppliedRule = rule,
                StateAfter = stateAfter,
                Steps = steps,
                IsFinal = isFinal
            };

        public static StepResult NoRule(string state, char symbol, int position, int steps) =>
            new StepResult
            {
                Kind = StepKind.NoRule,
                StateBefore = state,
                SymbolBefore = symbol,
                PositionBefore = position,
                Steps = steps,
                Message = $"Нет правила для состояния «{state}» и символа «{symbol}»."
            };

        public static StepResult Stopped(string reason) =>
            new StepResult { Kind = StepKind.Stopped, Message = reason };
    }

    public enum InstantRunOutcome { NotStarted, Completed, NoRule, Looped, MaxStepsReached }

    public sealed class InstantRunResult
    {
        public InstantRunOutcome Outcome { get; set; }
        public int Steps { get; set; }
    }
}
