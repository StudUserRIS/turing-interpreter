using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Интерпретатор_машины_Тьюринга.Core;

namespace Интерпретатор_машины_Тьюринга
{
    /// <summary>
    /// UI-координатор выполнения машины Тьюринга.
    ///
    /// После рефакторинга этот partial больше НЕ содержит бизнес-логики
    /// машины — она вынесена в доменный класс <see cref="TuringMachine"/>.
    /// Здесь остаются только обязанности UI:
    ///   • запустить/остановить таймер шагов;
    ///   • перерисовать ленту и таблицу состояний;
    ///   • показать диалог ошибки/успеха;
    ///   • заблокировать/разблокировать элементы формы при выполнении.
    ///
    /// Связь с доменом — через делегаты-провайдеры, которые передают в машину
    /// способ читать текущий символ ленты, искать правило в DataGridView и
    /// двигать головку. Это устраняет смешение уровней абстракции, на которое
    /// указывал разбор архитектуры (SRP/Separation of Concerns).
    /// </summary>
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля движка выполнения (UI-слой)
        // ──────────────────────────────────────────────────────────
        private const int VerySlowSpeed = 500;
        private const int SlowSpeed     = 250;
        private const int MediumSpeed   = 100;
        private const int FastSpeed     = 50;
        private const int VeryFastSpeed = 10;

        private System.Windows.Forms.Timer executionTimer;
        private bool isExecuting      = false;
        private bool isPaused         = false;
        private bool isProcessingStep = false;
        private ExecutionState currentExecution;
        private int executionSpeed = 500;

        // Доменная модель машины. Создаётся лениво при первом запуске и
        // переиспользуется между прогонами. Все провайдеры подключаются
        // в InitializeTuringMachine, вызываемом из StartExecution/SingleStep/Instant.
        private TuringMachine _machine;

        private Button btnInstantRun;
        private Button btnPause;
        private Button btnRun;
        private Button btnStep;
        private Button btnStop;

        // ──────────────────────────────────────────────────────────
        // Подготовка экземпляра доменной машины и провайдеров
        // ──────────────────────────────────────────────────────────
        private TuringMachine GetOrCreateMachine()
        {
            if (_machine == null) _machine = new TuringMachine();
            _machine.RuleProvider          = (state, symbol) => FindRule(state, symbol);
            _machine.CurrentSymbolProvider = () => tapeControl.tapeRenderer.GetCurrentSymbol();
            _machine.CurrentSymbolWriter   = (sym) => tapeControl.tapeRenderer.SetCurrentSymbol(sym);
            _machine.MoveHeadLeftAction    = () => tapeControl.tapeRenderer.MoveHeadLeft();
            _machine.MoveHeadRightAction   = () => tapeControl.tapeRenderer.MoveHeadRight();
            return _machine;
        }

        // ──────────────────────────────────────────────────────────
        // Запуск / останов выполнения
        // ──────────────────────────────────────────────────────────
        private void StartExecution()
        {
            if (isExecuting && isPaused)
            {
                isPaused = false;
                executionTimer.Start();
                UpdateButtonsState();
                return;
            }
            if (isExecuting) return;

            var initialState = states.FirstOrDefault(s => s.IsInitial);
            if (initialState == null)
            {
                ShowErrorDialog("Не задано начальное состояние машины Тьюринга. Отметьте одно из состояний как начальное и попробуйте снова.");
                return;
            }

            int headPos = tapeControl.tapeRenderer.GetHeadPosition();
            currentExecution = new ExecutionState
            {
                CurrentState = initialState.Name,
                HeadPosition = headPos,
                Steps = 0
            };

            GetOrCreateMachine().Reset(initialState.Name, headPos);

            isExecuting = true;
            isPaused = false;
            UpdateButtonsState();
            stateTable.Enabled = false;
            alphabetTextBox.Enabled = false;
            tapeControl.Enabled = false;
            executionTimer.Interval = executionSpeed;
            executionTimer.Start();
        }

        private void StopExecution()
        {
            if (!isExecuting) return;
            this.ActiveControl = null;
            executionTimer.Stop();
            _machine?.Stop();
            isExecuting = false;
            isPaused = false;
            isProcessingStep = false;
            currentStepCell = null;
            stateTable.Enabled = true;
            alphabetTextBox.Enabled = true;
            tapeControl.Enabled = true;
            UpdateButtonsState();
            stateTable.Invalidate();
            UpdateStatusBar();
        }

        private async void ExecuteStep()
        {
            if (currentExecution == null || tapeControl == null || !isExecuting || isProcessingStep) return;
            try
            {
                isProcessingStep = true;
                await ExecuteStepAsync();
            }
            finally
            {
                isProcessingStep = false;
            }
        }

        private async void ExecuteSingleStep()
        {
            if (isProcessingStep) return;
            try
            {
                isProcessingStep = true;
                this.ActiveControl = null;
                if (!isExecuting)
                {
                    var initialState = states.FirstOrDefault(s => s.IsInitial);
                    if (initialState == null)
                    {
                        ShowErrorDialog("Не задано начальное состояние машины Тьюринга. Отметьте одно из состояний как начальное и попробуйте снова.");
                        return;
                    }
                    int headPos = tapeControl.tapeRenderer.GetHeadPosition();
                    currentExecution = new ExecutionState
                    {
                        CurrentState = initialState.Name,
                        HeadPosition = headPos,
                        Steps = 0
                    };
                    GetOrCreateMachine().Reset(initialState.Name, headPos);
                    isExecuting = true;
                    isPaused = true;
                }
                await ExecuteStepAsync();
                if (isExecuting)
                {
                    executionTimer.Stop();
                    isPaused = true;
                }
                UpdateButtonsState();
            }
            finally
            {
                isProcessingStep = false;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Мгновенное выполнение — теперь полностью делегировано домену.
        // UI-слой только: бэкап ленты, обновление визуализации, диалоги.
        // ──────────────────────────────────────────────────────────
        private void ExecuteInstant()
        {
            try
            {
                this.ActiveControl = null;
                if (isExecuting)
                {
                    ShowWarningDialog("Невозможно выполнить мгновенный запуск — программа уже выполняется.");
                    return;
                }
                var initialState = states.FirstOrDefault(s => s.IsInitial);
                if (initialState == null)
                {
                    ShowErrorDialog("Не задано начальное состояние машины Тьюринга. Отметьте одно из состояний как начальное и попробуйте снова.");
                    return;
                }
                if (!HasAnyRules())
                {
                    ShowErrorDialog("В таблице переходов нет ни одного правила — выполнять нечего.");
                    return;
                }

                var tapeBackup = new Dictionary<int, char>();
                foreach (var cell in tapeControl.tapeRenderer._tape)
                    tapeBackup[cell.Key] = cell.Value.Value;
                int headPosBackup = tapeControl.tapeRenderer.GetHeadPosition();

                var machine = GetOrCreateMachine();
                machine.Reset(initialState.Name, headPosBackup);
                currentExecution = new ExecutionState
                {
                    CurrentState = machine.CurrentState,
                    HeadPosition = machine.HeadPosition,
                    Steps = 0
                };

                try
                {
                    var summary = machine.RunInstant(10000, OnInstantStep);
                    currentExecution.CurrentState = machine.CurrentState;
                    currentExecution.HeadPosition = machine.HeadPosition;
                    currentExecution.Steps = machine.Steps;

                    switch (summary.Outcome)
                    {
                        case InstantRunOutcome.Looped:
                            RestoreTape(tapeBackup, headPosBackup);
                            ShowWarningDialog("Алгоритм зациклен — выполнение остановлено.");
                            break;

                        case InstantRunOutcome.NoRule:
                            // Нет правила — машина мягко остановилась, ленту НЕ откатываем.
                            // Это ожидаемый сценарий завершения программ без явного qF.
                            break;

                        case InstantRunOutcome.MaxStepsReached:
                            RestoreTape(tapeBackup, headPosBackup);
                            actionHistory.Add($"Шаг {machine.Steps}: Превышено максимальное количество шагов (10000)");
                            ShowWarningDialog("Достигнут лимит в 10000 шагов — алгоритм, вероятно, зациклен.");
                            break;

                        case InstantRunOutcome.Completed:
                            actionHistory.Add($"Шаг {machine.Steps}: Программа завершена - достигнуто конечное состояние");
                            ShowSuccessDialog($"Программа завершена за {machine.Steps} шагов.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    RestoreTape(tapeBackup, headPosBackup);
                    actionHistory.Add($"Ошибка при мгновенном выполнении: {ex.Message}");
                    ShowErrorDialog($"Ошибка при мгновенном выполнении: {ex.Message}");
                }
                finally
                {
                    UpdateVisualization();
                    UpdateStatusBar();
                }
            }
            catch (Exception ex)
            {
                actionHistory.Add($"Неожиданная ошибка: {ex.Message}");
                ShowErrorDialog($"Неожиданная ошибка: {ex.Message}");
            }
        }

        private void OnInstantStep(StepResult step)
        {
            if (step.Kind != StepKind.Executed) return;
            string symbolDisplay = step.SymbolBefore == ' ' ? "_" : step.SymbolBefore.ToString();
            string directionDisplay = step.AppliedRule.Direction switch
            {
                '→' => "вправо",
                '←' => "влево",
                _   => "на месте"
            };
            actionHistory.Add(
                $"Шаг {step.Steps - 1}: Состояние '{step.StateBefore}', " +
                $"Позиция {step.PositionBefore}, " +
                $"Символ '{symbolDisplay}' → '{step.AppliedRule.NewSymbol}', " +
                $"Движение '{directionDisplay}', " +
                $"Новое состояние '{step.StateAfter}'");
        }

        // ──────────────────────────────────────────────────────────
        // Поиск правила в таблице переходов (UI-сторона домена).
        // Доменный класс не знает про DataGridView, поэтому ищем здесь
        // и возвращаем уже разобранный TransitionRule.
        // ──────────────────────────────────────────────────────────
        private TransitionRule FindRule(string currentState, char currentSymbol)
        {
            char symbolToFind = currentSymbol == ' ' ? '_' : currentSymbol;
            foreach (DataGridViewRow row in stateTable.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Cells["State"].Value?.ToString() == currentState)
                {
                    if (stateTable.Columns.Contains(symbolToFind.ToString()))
                    {
                        string ruleText = row.Cells[symbolToFind.ToString()].Value?.ToString();
                        if (!string.IsNullOrEmpty(ruleText))
                            return ParseRule(ruleText);
                    }
                }
            }
            return null;
        }

        private TransitionRule ParseRule(string ruleText)
        {
            // Делегирование чистой функции домена. Fallback-состояние —
            // текущее, если в правиле не указано явное.
            string fallback = currentExecution?.CurrentState ?? "";
            return TuringMachine.ParseRule(ruleText, fallback);
        }

        private bool IsFinalState(string stateName) => TuringMachine.IsFinalState(stateName);

        // ──────────────────────────────────────────────────────────
        // Скорость и визуализация
        // ──────────────────────────────────────────────────────────
        private void SetExecutionSpeed(int speed, ToolStripMenuItem selectedItem)
        {
            this.ActiveControl = null;
            executionSpeed = speed;
            if (currentSpeedItem != null)
                currentSpeedItem.Checked = false;
            currentSpeedItem = selectedItem;
            currentSpeedItem.Checked = true;
            btnSpeed.Text = $"Скорость {currentSpeedItem.Text} ▼";
            if (isExecuting)
            {
                executionTimer.Stop();
                executionTimer.Interval = executionSpeed;
                executionTimer.Start();
            }
            else
            {
                executionTimer.Interval = executionSpeed;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Один шаг в режиме «по таймеру» / «вручную».
        // Логика шага полностью делегирована TuringMachine.Step();
        // здесь только запись в историю, обновление UI и диалоги.
        // ──────────────────────────────────────────────────────────
        private async Task ExecuteStepAsync()
        {
            if (currentExecution == null) return;
            var machine = GetOrCreateMachine();

            // Если по какой-то причине доменная машина была остановлена раньше
            // (например, после реассайна ссылки или сбоя), синхронизируем её
            // с текущим UI-состоянием перед очередным шагом.
            if (!machine.IsRunning)
                machine.Reset(currentExecution.CurrentState, tapeControl.tapeRenderer.GetHeadPosition());

            var step = machine.Step();
            await Task.Yield(); // даём UI-потоку «дышать», эквивалент исходного async-контракта

            switch (step.Kind)
            {
                case StepKind.NoRule:
                    actionHistory.Add($"Шаг {machine.Steps}: Остановка - нет правила для состояния " +
                                      $"'{step.StateBefore}' и символа '{step.SymbolBefore}'");
                    StopExecution();
                    ShowWarningDialog(step.Message + " Выполнение остановлено.");
                    return;

                case StepKind.Executed:
                    string symbolDisplay   = step.SymbolBefore == ' ' ? "_" : step.SymbolBefore.ToString();
                    string directionDisplay = step.AppliedRule.Direction == '→' ? "вправо"
                                            : step.AppliedRule.Direction == '←' ? "влево"
                                            : "на месте";
                    actionHistory.Add(
                        $"Шаг {step.Steps - 1}: Состояние '{step.StateBefore}', " +
                        $"Позиция {step.PositionBefore}, " +
                        $"Символ '{symbolDisplay}' → '{step.AppliedRule.NewSymbol}', " +
                        $"Движение '{directionDisplay}', " +
                        $"Новое состояние '{step.StateAfter}'");

                    currentExecution.CurrentState = machine.CurrentState;
                    currentExecution.HeadPosition = machine.HeadPosition;
                    currentExecution.Steps = machine.Steps;
                    UpdateVisualization();

                    if (step.IsFinal)
                    {
                        actionHistory.Add($"Шаг {machine.Steps}: Программа завершена - достигнуто конечное состояние");
                        StopExecution();
                        ShowSuccessDialog($"Программа завершена за {machine.Steps} шагов. Достигнуто конечное состояние.");
                    }
                    return;
            }
        }

        private void UpdateVisualization()
        {
            if (currentExecution == null) return;
            tapeControl.tapeRenderer.Invalidate();
            string currentState = currentExecution.CurrentState;
            char currentSymbol = tapeControl.tapeRenderer.GetCurrentSymbol();
            string symbolKey = currentSymbol == ' ' ? "_" : currentSymbol.ToString();

            currentStepCell = null;
            foreach (DataGridViewRow row in stateTable.Rows)
            {
                string stateName = row.Cells["State"].Value?.ToString();
                if (stateName == currentState && stateTable.Columns.Contains(symbolKey))
                {
                    currentStepCell = row.Cells[symbolKey];
                    stateTable.CurrentCell = currentStepCell;
                    stateTable.FirstDisplayedScrollingRowIndex = row.Index;
                    break;
                }
            }
            UpdateStatusBar();
            stateTable.Invalidate();
        }

        private void UpdateStatusBar()
        {
            if (currentExecution == null)
            {
                executionStatusLabel.Text = "Готов";
                return;
            }
            executionStatusLabel.Text = $"Шаг: {currentExecution.Steps} | Состояние: {currentExecution.CurrentState} | Позиция: {currentExecution.HeadPosition}";
        }

        public void UpdateCellValue(int index, char value)
        {
            if (value == ' ')
                cellValues.Remove(index);
            else
                cellValues[index] = value;
        }
    }
}
