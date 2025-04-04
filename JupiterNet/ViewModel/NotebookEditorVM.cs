﻿using JupyterNetClient;
using JupyterNetClient.Nbformat;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace JupiterNet.ViewModel
{
    public class NotebookEditorVM : ViewModelBase
    {
        private const string PythonFolderSetting = "PYTHON_FOLDER";
        private const string REGISTRY_KEY = @"SOFTWARE\jupyternet";

        public class KernelItem
        {
            public string Key { get; set; }
            public string Name { get; set; }
        }

        public ObservableCollection<KernelItem> Kernels { get; private set; }
        public KernelItem SelectedKernel { get; set; }
        public string KernelStatus { get; set; }
        public string KernelName { get; set; }

        private Notebook _notebookModel;

        public string DocumentTitle => CurrentNotebook == null ? string.Empty : " - " + _notebookModel.GetTitle();
        public string DocumentCompleteFileName => CurrentNotebook == null ? string.Empty : _notebookModel.GetFileName();
        public NotebookVM CurrentNotebook { get; private set; } 
        public NotebookVM.CellVM SelectedCell { get; set; }
        public bool Multiline
        {
            get => _multiline;
            set
            {
                _multiline = value;
                OnPropertyChanged(nameof(Multiline));
            }
        }
        public bool EditMode { get; private set; }
        public bool InsertMode => !EditMode;

        public event EventHandler InitializationCompleted;

        public ObservableCollection<string> CompleteWords { get; } = new ObservableCollection<string>();

        public RelayCommand NewNotebookCommand { get; }
        public RelayCommand OpenNotebookCommand { get; }
        public RelayCommand SaveNotebookCommand { get; }
        public RelayCommand SaveAsNotebookCommand { get; }
        public RelayCommand InsertCodeCommand { get; }
        public RelayCommand InsertCommentCommand { get; }
        public RelayCommand InsertOrCompleteEditCommand { get; }
        public RelayCommand ToggleMultilineModeCommand { get; }
        public RelayCommand BeginEditCellCommand { get; }
        public RelayCommand CompleteEditCellCommand { get; }
        public RelayCommand CancelEditCellCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand RunCellCommand { get; }
        public RelayCommand RunAllCellsCommand { get; }
        public RelayCommand RunScriptCommand { get; }
        public RelayCommand KernelInterruptCommand { get; }
        public RelayCommand CutCommand { get; }
        public RelayCommand CopyCommand { get; }
        public RelayCommand PasteCommand { get; }
        public RelayCommand MoveCellDownCommand { get; }
        public RelayCommand MoveCellUpCommand { get; }
        public RelayCommand CompleteCodeCommand { get; }
        public RelayCommand AboutCommand { get; }

        private JupyterClient _client;
        private readonly Dispatcher _currentDispatcher;
        private bool _interruptRunAll;
        private bool _multiline;
        private KernelState? _kernelState;
        private readonly IViewServices _services;
        
        public NotebookEditorVM(IViewServices dialogs)
        {
            _currentDispatcher = Application.Current.Dispatcher;
            _services = dialogs;
            EditMode = false;

            Predicate<object> kernelReadyPredicate = _ => _kernelState == KernelState.idle;
            Predicate<object> selectedCellPred = _ => SelectedCell != null;
            Predicate<object> selectedRunnableCellPred = _ => SelectedCell != null && SelectedCell.CanExecute;
            
            NewNotebookCommand = new RelayCommand((_) => NewNotebook());
            OpenNotebookCommand = new RelayCommand((_) => OpenNotebook());
            SaveNotebookCommand = new RelayCommand((_) => SaveNotebook());
            SaveAsNotebookCommand = new RelayCommand((_) => SaveAsNotebook());
            InsertCodeCommand = new RelayCommand((_) => InsertCode(), kernelReadyPredicate);
            InsertCommentCommand = new RelayCommand((_) => InsertComment(), kernelReadyPredicate);
            ToggleMultilineModeCommand = new RelayCommand(_ => Multiline = !Multiline);
            BeginEditCellCommand = new RelayCommand((_) => BeginEditCell(), 
                Utils.And(kernelReadyPredicate, selectedCellPred));
            CompleteEditCellCommand = new RelayCommand((_) => CompleteEditCell(), kernelReadyPredicate);
            CancelEditCellCommand = new RelayCommand((_) => CancelEditCell(), kernelReadyPredicate);
            InsertOrCompleteEditCommand = new RelayCommand((_) => { if (EditMode) CompleteEditCell(); else InsertCode(); }, kernelReadyPredicate);
            DeleteCommand = new RelayCommand((_) => DeleteCell(),
                Utils.And(kernelReadyPredicate, selectedCellPred));
            RunCellCommand = new RelayCommand((_) => RunSelectedCell(),
                Utils.And(kernelReadyPredicate, selectedRunnableCellPred));
            RunAllCellsCommand = new RelayCommand((_) => RunAllCells(), kernelReadyPredicate);
            RunScriptCommand = new RelayCommand((_) => RunScript(), kernelReadyPredicate);
            KernelInterruptCommand = new RelayCommand((_) => KernelInterrupt());
            CutCommand = new RelayCommand((_) => CutCell());
            CopyCommand = new RelayCommand((_) => CopyCell());
            PasteCommand = new RelayCommand((_) => PasteCell());
            MoveCellDownCommand = new RelayCommand((_) => MoveCellDown());
            MoveCellUpCommand = new RelayCommand((_) => MoveCellUp());
            CompleteCodeCommand = new RelayCommand((_) => CompleteCode());
            AboutCommand = new RelayCommand((_) => _services.ShowAbout());
        }

        public void InitializeKernel()
        {
            UpdateStatus("Initiating");
            OnPropertyChanged(nameof(KernelStatus));

            try
            {
                var pythonFolder = GetSetting(PythonFolderSetting);
                SetEnvironmentVariables(@"PATH", pythonFolder);
                _client = new JupyterClient(pythonFolder);
            }
            catch (Exception)
            {
                //assuming python.exe not found, ask python.exe location
                _services.ShowError(@"Python directory not found. You will be asked to select the location of the file python.exe.");
                var pythonFolder = _services.AskPythonFolder();
                if (string.IsNullOrEmpty(pythonFolder))
                {
                    throw new Exception("Python folder not provided");
                }
                _client = new JupyterClient(pythonFolder);
                SetSetting(PythonFolderSetting, pythonFolder);
            }

            _client.OnStatus += HandleStatusMessage;
            _client.OnOutputMessage += HandleOutputMessage;
            _client.OnInputRequest += AskInput;

            Task.Run(() =>
            {
                var kernels = _client.GetKernels();
                var kernelId = string.Empty;
                switch (kernels.Count)
                {
                    case 0:
                        throw new Exception("No kernels found");

                    case 1:
                        kernelId = kernels.Keys.First();
                        break;

                    default:
                    {
                        Kernels = new ObservableCollection<KernelItem>(
                            kernels.Select(a => new KernelItem { Key = a.Key, Name = a.Value.spec.display_name }));
                        kernelId = _services.SelectKernel(this);
                        if (string.IsNullOrEmpty(kernelId))
                        {
                            throw new Exception("No kernel selected");
                        }

                        break;
                    }
                }

                _client.StartKernel(kernelId);

                KernelName = kernels[kernelId].spec.display_name;
                OnPropertyChanged(nameof(KernelName));
                _currentDispatcher.Invoke(NewNotebook);
                UpdateStatus("Ready");
                InitializationCompleted?.Invoke(this, null);
            });
        }

        private void SetEnvironmentVariables(string variable, string value)
        {
            var target = EnvironmentVariableTarget.Process;
            var currentValue = Environment.GetEnvironmentVariable(variable, target);
            if (currentValue != null)
            {
                var splitValues = currentValue.Split(Path.PathSeparator);
                if (splitValues.Any(s => !string.IsNullOrEmpty(s) && Path.GetFullPath(s) == Path.GetFullPath(value)))
                {
                    return;
                }

                var combinedVariables = Path.GetFullPath(value) + Path.PathSeparator + currentValue;
                Environment.SetEnvironmentVariable(
                    variable,
                    combinedVariables,
                    target);
            }
            else
            {
                Environment.SetEnvironmentVariable(
                    variable,
                    Path.GetFullPath(value),
                    target);
            }
        }

        private void HandleStatusMessage(object sender, KernelState kernelState)
        {
            _kernelState = kernelState;
            _currentDispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
            UpdateStatus(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(kernelState.ToString()));
        }

        private void HandleOutputMessage(object sender, JupyterMessage message)
        {
            switch (message.header.msg_type)
            {
                case JupyterMessage.Header.MsgType.execute_input:
                    _notebookModel.FindParentCell(message).UpdateFromExecuteInputMessage(message);
                    break;

                case JupyterMessage.Header.MsgType.execute_result:
                case JupyterMessage.Header.MsgType.display_data:
                case JupyterMessage.Header.MsgType.stream:
                case JupyterMessage.Header.MsgType.error:
                    _notebookModel.FindParentCell(message).AddOutputFromMessage(message);
                    break;

                case JupyterMessage.Header.MsgType.execute_reply:
                    var parentCell = _notebookModel.FindParentCell(message);
                    CurrentNotebook.SetCellExecutionCompleted(parentCell, message.content as JupyterMessage.ExecuteReplyContent);
                    break;

                case JupyterMessage.Header.MsgType.complete_reply:
                    var content = (JupyterMessage.CompleteReply) message.content;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CompleteWords.Clear();
                        content.matches.ForEach(s => CompleteWords.Add(s));
                    });
                    _services.SetCodeCompletion(content.matches, content.cursor_start, content.cursor_end);
                    OnPropertyChanged(nameof(CompleteWords));
                    break;

                default:
                {
                    break;
                }
            }
        }

        public void OnClosing(object sender, CancelEventArgs e)
        {
            UpdateStatus("Closing");
            if (!SaveFileIfRequired())
            {
                e.Cancel = true;
                return;
            }
            _client.Shutdown();
            e.Cancel = false;
            UpdateStatus("Done");
        }

        private void UpdateStatus(string status)
        {
            KernelStatus = status;
            OnPropertyChanged(nameof(KernelStatus));
        }

        private void SetEditMode(bool value)
        {
            EditMode = value;
            OnPropertyChanged(nameof(EditMode));
            OnPropertyChanged(nameof(InsertMode));
        }

        private void NewNotebook()
        {
            if (!SaveFileIfRequired())
            {
                return;
            }
            _notebookModel = new Notebook(_client.KernelSpec, _client.KernelInfo.language_info);
            _currentDispatcher.Invoke(() => CurrentNotebook = new NotebookVM(_notebookModel, _currentDispatcher));
            OnPropertyChanged(nameof(CurrentNotebook));
            DocumentNameChanged();
        }

        private void OpenNotebook()
        {
            if (!SaveFileIfRequired())
            {
                return;
            }
            var fileName = _services.OpenFile();
            if (!string.IsNullOrEmpty(fileName))
            {
                try
                {
                    _notebookModel = Notebook.ReadFromFile(fileName);
                    CurrentNotebook = new NotebookVM(_notebookModel, _currentDispatcher);
                    OnPropertyChanged(nameof(CurrentNotebook));
                    DocumentNameChanged();
                }
                catch (Exception e)
                {
                    //TODO display proper error message
                    Console.WriteLine(e);
                }
            }
        }

        private void SaveAsNotebook()
        {
            var fileName = _services.AskFileName();
            if (!string.IsNullOrEmpty(fileName))
            {
                _notebookModel.Save(fileName);
                DocumentNameChanged();
            }
        }

        private void SaveNotebook()
        {
            if (_notebookModel.HasFileName())
            {
                _notebookModel.Save(_notebookModel.GetFileName());
            }
            else
            {
                SaveAsNotebook();
            }
        }

        private bool SaveFileIfRequired()
        {
            if (_notebookModel?.IsDirty() ?? false)
            {
                var answer = _services.AskSaveDocument(_notebookModel.GetTitle());
                if (answer == DialogResult.Save)
                {
                    SaveNotebook();
                }
                else if (answer == DialogResult.Cancel)
                {
                    return false;
                }
            }
            return true;
        }

        private void InsertCode()
        {
            var code = _services.GetInput();
            if (!string.IsNullOrEmpty(code))
            {
                var cell = _notebookModel.AddCode(code);
                RunCell(cell);
            }
        }

        private void InsertComment()
        {
            var text = _services.GetInput();
            if (!string.IsNullOrEmpty(text))
            {
                _notebookModel.AddMarkdown(text);
            }
        }

        private void RunSelectedCell()
        {
            if (SelectedCell != null && SelectedCell.AttachedCell is CodeCell cell)
            {
                RunCell(cell);
            }
        }

        private void RunAllCells()
        {
            var cellsToExecute = CurrentNotebook.Cells
                .Where(c => c.CanExecute)
                .Select(c => (CodeCell) c.AttachedCell)
                .ToList();

            Task.Run(() =>
            {
                _interruptRunAll = false;
                foreach (var cell in cellsToExecute)
                {
                    if (_interruptRunAll)
                    {
                        break;
                    }
                    RunCell(cell);
                }
            });
        }

        private void RunScript()
        {
            var file = _services.SelectPythonFile();
            if (!string.IsNullOrEmpty(file))
            {
                var cell = _notebookModel.AddCode($"%run {file}");
                RunCell(cell);
            }
        }

        private void RunCell(CodeCell cell)
        {
            CurrentNotebook.SetCellExecutionStarted(cell);
            cell.ClearOutputs();
            _client.Execute(cell);
        }

        private void DeleteCell()
        {
            if (SelectedCell == null)
            {
                return;
            }
            if (SelectedCell is NotebookVM.InputCellVM)
            {
                _notebookModel.DeleteCell(SelectedCell.AttachedCell);
            }
            else if (SelectedCell.AttachedCell is CodeCell codeCell)
            {
                codeCell.DeleteOutput(SelectedCell.AttachedCellOutput);
            }
        }

        private void BeginEditCell()
        {
            if (SelectedCell is NotebookVM.InputCellVM inputCellVM)
            {
                SetEditMode(true);
                _services.BeginEditCell(inputCellVM.Value);
            }
        }

        private void CompleteEditCell()
        {
            if (SelectedCell is NotebookVM.InputCellVM inputCellVM)
            {
                inputCellVM.AttachedCell.UpdateValue(_services.GetInput());
                if (inputCellVM.CanExecute)
                {
                    RunCell(inputCellVM.AttachedCell as CodeCell);
                }
            }
            SetEditMode(false);
        }

        private void CancelEditCell()
        {
            _services.CancelEditCell();
            SetEditMode(false);
        }

        private void CutCell()
        {
            CopyCell();
            DeleteCell();
        }

        private void CopyCell()
        {
            switch (SelectedCell)
            {
                case NotebookVM.InputCellVM inputCellVm:
                    Clipboard.SetText(inputCellVm.Value);
                    break;

                case NotebookVM.TextCellVM textCellVm:
                    Clipboard.SetText(textCellVm.Value);
                    break;

                case NotebookVM.ImageCellVM imageCellVm:
                    //can't cut/copy/paste an output cell containing an image
                    break;
            }
        }

        private void PasteCell()
        {
            if (Clipboard.ContainsText())
            {
                _services.SetInputText(Clipboard.GetText());
            }
        }

        private void MoveCellDown() => 
            _notebookModel.MoveCellDown(SelectedCell.AttachedCell);

        private void MoveCellUp() =>
            _notebookModel.MoveCellUp(SelectedCell.AttachedCell);

        private void CompleteCode()
        {
            var code = _services.PeekInput();
            _client.Complete(code, code.Length);
        }

        private void KernelInterrupt()
        {
            _interruptRunAll = true;
            _client.KernelInterrupt();
        }

        private void AskInput(object sender, (string prompt, bool password) e)
        {
            var value = _services.AskString(e.prompt, e.password);
            _client.SendInputReply(value);
        }

        private void DocumentNameChanged()
        {
            OnPropertyChanged(nameof(DocumentCompleteFileName));
            OnPropertyChanged(nameof(DocumentTitle));
        }

        private static string GetSetting(string key)
        {
            var registryKey = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
            var result = registryKey.GetValue(key)?.ToString() ?? "";
            registryKey.Close();
            return result;
        }

        private static void SetSetting(string key, string value)
        {
            var registryKey = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY);
            registryKey.SetValue(key, value);
            registryKey.Close();
        }
    }
}