using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Интерпретатор_машины_Тьюринга
{
    public partial class Form1
    {
        // ──────────────────────────────────────────────────────────
        // Поля файлового менеджера
        // ──────────────────────────────────────────────────────────
        private string currentFilePath = null;
        private Button btnFile;

        // ──────────────────────────────────────────────────────────
        // Новый / Открыть / Сохранить
        // ──────────────────────────────────────────────────────────
        private void NewFile()
        {
            if (isExecuting)
            {
                ShowWarningDialog("Невозможно создать новый файл во время выполнения программы.");
                return;
            }
            this.ActiveControl = null;
            if (currentFilePath != null || cellValues.Count > 0 || states.Count > 1)
            {
                if (!ShowConfirmDialog("Текущие данные будут потеряны. Продолжить?", "Новый файл")) return;
            }
            currentFilePath = null;
            cellValues.Clear();
            states.Clear();
            states.Add(new TuringState { Name = "q0", IsInitial = true });
            tapeControl.tapeRenderer.ResetTape();
            alphabetTextBox.Text = "";
            var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
            if (problemTextBox != null) problemTextBox.Text = "";
            commentTextBox.Text = "";
            tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
            UpdateStateTable();
            UpdateStateTableColumns();
            UpdateButtonsState();
            actionHistory.Clear();
        }

        private void SaveFile()
        {
            if (isExecuting)
            {
                ShowWarningDialog("Невозможно сохранить программу во время выполнения.");
                return;
            }
            if (currentFilePath == null) { SaveFileAs(); return; }
            SaveToFile(currentFilePath);
        }

        private void OpenFile()
        {
            actionHistory.Clear();
            this.ActiveControl = null;
            if (isExecuting)
            {
                ShowWarningDialog("Сначала остановите выполнение программы, чтобы открыть другой файл.");
                return;
            }
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.Filter = FileFilter;
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(openDialog.FileName);
                        var data = JsonConvert.DeserializeObject<TuringMachineData>(json);
                        if (data.TapeContent != null)
                        {
                            tapeControl.tapeRenderer._tape.Clear();
                            foreach (var cell in data.TapeContent)
                                tapeControl.tapeRenderer._tape[cell.Key] = new TapeCell(cell.Value == '_' ? ' ' : cell.Value);
                        }
                        tapeControl.InitializeTapeContent(new StringBuilder().ToString());
                        tapeControl.SetHeadPosition(data.HeadPosition);
                        alphabetTextBox.Text = data.Alphabet ?? "01ABC";
                        tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";
                        states = new BindingList<TuringState>(data.States ?? new List<TuringState>());
                        UpdateStateTableColumns();
                        UpdateStateTable();

                        if (data.TransitionRules != null)
                        {
                            foreach (DataGridViewRow row in stateTable.Rows)
                            {
                                if (row.IsNewRow) continue;
                                string state = row.Cells["State"].Value?.ToString();
                                if (state != null && data.TransitionRules.ContainsKey(state))
                                {
                                    foreach (var rule in data.TransitionRules[state])
                                    {
                                        if (stateTable.Columns.Contains(rule.Key))
                                            row.Cells[rule.Key].Value = rule.Value;
                                    }
                                }
                            }
                        }

                        var problemTextBox = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault();
                        if (problemTextBox != null) problemTextBox.Text = data.ProblemCondition ?? "";

                        if (commentTextBox != null)
                            commentTextBox.Text = this.Text.StartsWith("Конструктор МТ") ? "" : (data.Comment ?? "");

                        currentFilePath = openDialog.FileName;
                        UpdateButtonsState();
                    }
                    catch (JsonException)
                    {
                        ShowErrorDialog("Невозможно прочитать файл: неверный формат.");
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog($"Ошибка загрузки файла: {ex.Message}");
                    }
                }
            }
        }

        private void SaveFileAs()
        {
            if (isExecuting)
            {
                ShowWarningDialog("Невозможно сохранить программу во время выполнения.");
                return;
            }
            this.ActiveControl = null;
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = FileFilter;
                saveDialog.Title = "Сохранить файл машины Тьюринга";
                saveDialog.DefaultExt = "turing";
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    currentFilePath = saveDialog.FileName;
                    SaveToFile(currentFilePath);
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // Импорт / Экспорт
        // ──────────────────────────────────────────────────────────
        private void ImportProgram()
        {
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*";
                openDialog.Title = "Импорт программы машины Тьюринга";
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = File.ReadAllText(openDialog.FileName);
                        var data = JsonConvert.DeserializeObject<TuringMachineData>(json);
                        if (data == null) throw new Exception("Неверный формат файла программы");

                        NewFile();
                        alphabetTextBox.Text = data.Alphabet ?? "01ABC";
                        tapeControl.tapeRenderer.AllowedSymbols = alphabetTextBox.Text + "_";

                        if (data.TapeContent != null && data.TapeContent.Count > 0)
                        {
                            tapeControl.tapeRenderer.ResetTape();
                            foreach (var cell in data.TapeContent)
                                tapeControl.tapeRenderer._tape[cell.Key] = new TapeCell(cell.Value == '_' ? ' ' : cell.Value);
                            tapeControl.tapeRenderer.SetHeadPosition(data.HeadPosition);
                        }

                        UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault().Text = data.ProblemCondition ?? "";

                        if (commentTextBox != null)
                            commentTextBox.Text = this.Text.StartsWith("Конструктор МТ") ? "" : (data.Comment ?? "");

                        states.Clear();
                        if (data.States != null)
                        {
                            foreach (var state in data.States.OrderBy(s => s.Name))
                                states.Add(new TuringState { Name = state.Name, IsInitial = state.IsInitial, IsFinal = state.IsFinal });
                        }
                        UpdateStateTableColumns();
                        UpdateStateTable();

                        if (data.TransitionRules != null)
                        {
                            foreach (var stateRule in data.TransitionRules)
                            {
                                foreach (DataGridViewRow row in stateTable.Rows)
                                {
                                    if (row.Cells["State"].Value?.ToString() == stateRule.Key)
                                    {
                                        foreach (var rule in stateRule.Value)
                                        {
                                            if (stateTable.Columns.Contains(rule.Key))
                                                row.Cells[rule.Key].Value = rule.Value;
                                        }
                                        break;
                                    }
                                }
                            }
                        }

                        ShowSuccessDialog("Программа успешно импортирована.");
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog($"Ошибка при импорте программы: {ex.Message}");
                    }
                }
            }
        }

        private void ExportProgram()
        {
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*";
                saveDialog.Title = "Экспорт программы машины Тьюринга";
                saveDialog.DefaultExt = "txt";
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var data = new TuringMachineData
                        {
                            Alphabet = alphabetTextBox.Text,
                            TapeContent = tapeControl.tapeRenderer._tape.ToDictionary(
                                cell => cell.Key,
                                cell => cell.Value.Value == ' ' ? '_' : cell.Value.Value),
                            HeadPosition = tapeControl.tapeRenderer.GetHeadPosition(),
                            ProblemCondition = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault()?.Text ?? "",
                            Comment = commentTextBox?.Text ?? "",
                            States = states.ToList(),
                            TransitionRules = new Dictionary<string, Dictionary<string, string>>()
                        };
                        foreach (DataGridViewRow row in stateTable.Rows)
                        {
                            if (row.IsNewRow) continue;
                            string stateName = row.Cells["State"].Value?.ToString();
                            if (string.IsNullOrEmpty(stateName)) continue;
                            data.TransitionRules[stateName] = new Dictionary<string, string>();
                            foreach (DataGridViewCell cell in row.Cells)
                            {
                                if (cell.OwningColumn.Name != "State" && cell.Value != null)
                                    data.TransitionRules[stateName][cell.OwningColumn.Name] = cell.Value.ToString();
                            }
                        }
                        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                        File.WriteAllText(saveDialog.FileName, json);
                        ShowSuccessDialog("Программа успешно экспортирована.");
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog($"Ошибка при экспорте программы: {ex.Message}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // Сохранение в файл
        // ──────────────────────────────────────────────────────────
        private void SaveToFile(string filePath)
        {
            try
            {
                var tapeData = tapeControl.tapeRenderer._tape.ToDictionary(
                    cell => cell.Key,
                    cell => cell.Value.Value == ' ' ? '_' : cell.Value.Value);
                var rules = new Dictionary<string, Dictionary<string, string>>();
                foreach (DataGridViewRow row in stateTable.Rows)
                {
                    if (row.IsNewRow) continue;
                    string state = row.Cells["State"].Value?.ToString();
                    if (string.IsNullOrEmpty(state)) continue;
                    rules[state] = new Dictionary<string, string>();
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        if (cell.OwningColumn.Name != "State" && cell.Value != null)
                            rules[state][cell.OwningColumn.Name] = cell.Value.ToString();
                    }
                }
                string problemCondition = UslovieZadachi.Controls.OfType<TextBox>().FirstOrDefault()?.Text ?? "";
                var data = new TuringMachineData
                {
                    Alphabet = alphabetTextBox.Text,
                    TapeContent = tapeData,
                    HeadPosition = tapeControl.tapeRenderer.GetHeadPosition(),
                    States = states.ToList(),
                    TransitionRules = rules,
                    ProblemCondition = problemCondition,
                    Comment = commentTextBox?.Text ?? ""
                };
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
                ShowSuccessDialog("Конфигурация успешно сохранена.");
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"Ошибка сохранения: {ex.Message}");
            }
        }
    }
}
