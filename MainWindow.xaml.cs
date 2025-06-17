using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace ColorClickApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isCapturing = false;
    private ActionInfo? _currentActionInfoForCapture = null; // 用于在捕获模式下存储当前的 ActionInfo
    private const string ConfigFilePath = "config.json";

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private DispatcherTimer _colorMonitorTimer;
        private int _currentActionIndex = 0;
        private int _currentMonitorPositionIndex = 0;
        private bool _isMonitoringLoopActive = false;
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private IntPtr _lockedWindowHandle = IntPtr.Zero;
    private string _lockedProcessName = string.Empty;

    private ObservableCollection<TaskInfo> _tasks = new ObservableCollection<TaskInfo>();
    public ObservableCollection<ActionInfo> CurrentActions { get; set; }



    public MainWindow()
    {
        InitializeComponent();
        CurrentActions = new ObservableCollection<ActionInfo>();

        LoadConfig(); // 加载配置

        TaskComboBox.ItemsSource = _tasks;
        TaskComboBox.DisplayMemberPath = "TaskName";
        TaskComboBox.SelectedValuePath = "TaskName";
        ActionsDataGrid.ItemsSource = CurrentActions;

        RefreshProcessList();

        _colorMonitorTimer = new System.Windows.Threading.DispatcherTimer();
        _colorMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
        _colorMonitorTimer.Tick += ColorMonitorTimer_Tick;

        // 添加鼠标左键抬起事件处理
        this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;

        // 窗口关闭时保存配置
        this.Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveConfig();
    }

    private void SaveConfig()
    {
        var config = new AppConfig { Tasks = _tasks };
        var options = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(config, options);
        File.WriteAllText(ConfigFilePath, jsonString);
        Log("配置已保存。");
    }

    private void LoadConfig()
    {
        if (File.Exists(ConfigFilePath))
        {
            try
            {
                string jsonString = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(jsonString);
                if (config?.Tasks != null)
                {
                    _tasks.Clear();
                    foreach (var task in config.Tasks)
                    {
                        _tasks.Add(task);
                    }
                    Log("配置已加载。");
                }
                else
                {
                    Log("加载的配置为空或无效。");
                }
            }
            catch (JsonException ex)
            {
                Log($"加载配置失败: {ex.Message}");
            }
        }
        else
        {
            Log("配置文件不存在，将创建新配置。");
        }
    }

    // 辅助类，用于屏幕截图和获取像素颜色
    public static class ScreenCapture
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern int GetPixel(IntPtr hdc, int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        public static System.Windows.Media.Color GetPixelColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            int pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);
            System.Windows.Media.Color color = System.Windows.Media.Color.FromArgb(255, (byte)((pixel >> 0) & 0xff), (byte)((pixel >> 8) & 0xff), (byte)((pixel >> 16) & 0xff));
            return color;
        }
    }

    // 辅助类，用于模拟鼠标点击
    public static class MouseClicker
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;

        public static void DoLeftClick(int x, int y)
        {
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
            System.Threading.Thread.Sleep(200); // 鼠标按下持续 200 毫秒
            mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
        }
    }

    private void RefreshProcessList()
    {
        ProcessComboBox.Items.Clear();
        List<ProcessInfo> processes = new List<ProcessInfo>();

        EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
        {
            if (IsWindowVisible(hWnd))
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                StringBuilder sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);

                string windowTitle = sb.ToString();
                if (!string.IsNullOrWhiteSpace(windowTitle) && !processes.Any(p => p.Title == windowTitle))
                {
                    try
                    {
                        Process process = Process.GetProcessById(GetWindowProcessID(hWnd));
                        if (process != null && !string.IsNullOrWhiteSpace(process.ProcessName))
                        {
                            processes.Add(new ProcessInfo { Handle = hWnd, Title = windowTitle, ProcessName = process.ProcessName });
                        }
                    }
                    catch (ArgumentException) { /* Process might have exited */ }
                }
                return true;
            }
            return true;
        }, IntPtr.Zero);

        foreach (var p in processes.OrderBy(p => p.Title))
        {
            ProcessComboBox.Items.Add(p);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private int GetWindowProcessID(IntPtr hwnd)
    {
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        return (int)pid;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockedWindowHandle == IntPtr.Zero)
        {
            // Lock
            if (ProcessComboBox.SelectedItem is ProcessInfo selectedProcess)
            {
                _lockedWindowHandle = selectedProcess.Handle;
                _lockedProcessName = selectedProcess.ProcessName;

                ProcessComboBox.IsEnabled = false;
                RefreshButton.IsEnabled = false;
                LockButton.Content = "解锁";
                Log("已锁定窗口: " + selectedProcess.Title);
            }
            else
            {
                Log("请选择一个要锁定的应用程序。");
            }
        }
        else
        {
            // Unlock
            _lockedWindowHandle = IntPtr.Zero;
            _lockedProcessName = string.Empty;

            ProcessComboBox.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            LockButton.Content = "锁定";
            Log("已解锁窗口。");
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshProcessList();
        Log("进程列表已刷新。");
    }

    private void Log(string message)
    {
        LogTextBlock.Text += ($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        LogScrollViewer.ScrollToEnd();
    }

    private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Optional: Display some info about the selected process
        if (ProcessComboBox.SelectedItem is ProcessInfo selectedProcess)
        {
            Log($"已选择进程: {selectedProcess.Title} (PID: {GetWindowProcessID(selectedProcess.Handle)})");
        }
    }

    private void NewTaskButton_Click(object sender, RoutedEventArgs e)
    {
        // Implement new task creation logic
        // For simplicity, let's use a simple InputBox for now
        string taskName = Microsoft.VisualBasic.Interaction.InputBox("请输入新任务名称:", "新建任务", "新任务");

        if (!string.IsNullOrWhiteSpace(taskName))
        {
            if (_tasks.Any(t => t.TaskName == taskName))
            {
                Log($"任务 '{taskName}' 已存在。");
            }
            else
            {
                TaskInfo newTask = new TaskInfo { TaskName = taskName };
                _tasks.Add(newTask);

                // Add a blank action when a new task is created
                ActionInfo blankAction = new ActionInfo { ActionName = "" };
                newTask.Actions.Add(blankAction);
                CurrentActions.Add(blankAction);
                TaskComboBox.SelectedItem = newTask;
                Log($"已创建新任务: {taskName}");
            }
        }
        else
        {
            Log("任务名称不能为空。");
        }
    }

    private void TaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
        {
            CurrentActions.Clear();
            foreach (var action in selectedTask.Actions)
            {
                CurrentActions.Add(action);
            }
            Log($"已切换到任务: {selectedTask.TaskName}，加载了 {selectedTask.Actions.Count} 个动作。");
        }
        else
        {
            CurrentActions.Clear();
            Log("未选择任务，清空动作列表。");
        }
    }

    private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
        {
            var result = System.Windows.MessageBox.Show($"确定要删除任务 '{selectedTask.TaskName}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _tasks.Remove(selectedTask);
                if (_tasks.Count > 0)
                {
                    TaskComboBox.SelectedItem = _tasks[0];
                }
                else
                {
                    CurrentActions.Clear(); // 如果没有任务了，清空动作列表
                }
                Log($"已删除任务: {selectedTask.TaskName}");
            }
        }
        else
        {
            Log("请选择一个要删除的任务。");
        }
    }

    private void AddActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
        {
            // Implement add action logic
            // For simplicity, let's use a simple InputBox for now
            string actionName = Microsoft.VisualBasic.Interaction.InputBox("请输入新动作名称:", "新增动作", "新动作");

            if (!string.IsNullOrWhiteSpace(actionName))
            {
                ActionInfo newAction = new ActionInfo { ActionName = actionName };
                selectedTask.Actions.Add(newAction);
                CurrentActions.Add(newAction);
                Log($"已为任务 '{selectedTask.TaskName}' 添加动作: {actionName}");
            }
            else
            {
                Log("动作名称不能为空。");
            }
        }
        else
        {
            Log("请先选择一个任务或创建一个新任务。");
        }
    }

    private void AddActionToTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.CommandParameter is ActionInfo actionToAdd)
        {
            if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
            {
                // Create a new instance of ActionInfo to add, not the same object
                ActionInfo newAction = new ActionInfo { ActionName = actionToAdd.ActionName, MonitorPositions = new ObservableCollection<MonitorPosition>(actionToAdd.MonitorPositions ?? new ObservableCollection<MonitorPosition>()) };
                selectedTask.Actions.Add(newAction);
                Log($"已为任务 '{selectedTask.TaskName}' 复制动作: {newAction.ActionName}");
            }
            else
            {
                Log("请先选择一个任务或创建一个新任务。");
            }
        }
    }

    private void RemoveActionFromTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.CommandParameter is ActionInfo actionToRemove)
        {
            if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
            {
                selectedTask.Actions.Remove(actionToRemove);
                CurrentActions.Remove(actionToRemove);
                actionToRemove.IsEditable = true; // 允许再次编辑
                Log($"已从任务 '{selectedTask.TaskName}' 移除动作: {actionToRemove.ActionName}");
            }
            else
            {
                Log("请先选择一个任务或创建一个新任务。");
            }
        }
    }

    private async void ColorMonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (TaskComboBox.SelectedItem is not TaskInfo selectedTask || selectedTask.Actions.Count == 0)
        {
            return;
        }

        _isMonitoringLoopActive = true;

        // 循环比对所有动作
        while (_isMonitoringLoopActive)
        {
            if (_currentActionIndex >= selectedTask.Actions.Count)
            {
                _currentActionIndex = 0; // 从头开始循环
            }

            var currentAction = selectedTask.Actions[_currentActionIndex];

            if (currentAction.MonitorPositions != null && currentAction.MonitorPositions.Count > 0)
            {
                if (_currentMonitorPositionIndex >= currentAction.MonitorPositions.Count)
                {
                    _currentMonitorPositionIndex = 0; // 从头开始循环当前动作的监控位置
                }

                var monitorPosition = currentAction.MonitorPositions[_currentMonitorPositionIndex];

                if (monitorPosition.ExpectedColor.HasValue)
                {
                    int targetX = monitorPosition.X;
                    int targetY = monitorPosition.Y;

                    // 如果窗口已锁定，将相对坐标转换为屏幕绝对坐标
                    if (_lockedWindowHandle != IntPtr.Zero)
                    {
                        RECT windowRect;
                        if (GetWindowRect(_lockedWindowHandle, out windowRect))
                        {
                            targetX = windowRect.Left + monitorPosition.X;
                            targetY = windowRect.Top + monitorPosition.Y;
                        }
                        else
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log("无法获取锁定窗口的矩形信息，将使用原始坐标进行颜色检测。");
                            });
                        }
                    }
                    System.Windows.Media.Color screenColor = ScreenCapture.GetPixelColor(targetX, targetY);

                    if (screenColor == monitorPosition.ExpectedColor.Value)
                    {
                        // 颜色匹配，如果未在点击，则开始点击
                        if (!monitorPosition.IsClicking)
                        {
                            monitorPosition.IsClicking = true;
                            Log($"动作 '{currentAction.ActionName}' 位置({monitorPosition.X},{monitorPosition.Y}) 颜色匹配: {screenColor} == {monitorPosition.ExpectedColor.Value}");
                            // 启动一个新线程进行连续点击
                            await Task.Run(() => ContinuousClick(monitorPosition, currentAction.ClickInterval));
                        }
                    }
                    else
                    {
                        // 颜色不匹配，停止点击
                        Log($"动作 '{currentAction.ActionName}' 位置({monitorPosition.X},{monitorPosition.Y}) 颜色不匹配: {screenColor} != {monitorPosition.ExpectedColor.Value}");
                        monitorPosition.IsClicking = false;
                    }
                }

                _currentMonitorPositionIndex++;
            }
            else
            {
                // 如果当前动作没有监控位置，则直接跳到下一个动作
                _currentMonitorPositionIndex = 0; // 重置监控位置索引
            }

            // 检查是否需要切换到下一个动作
            if (currentAction.MonitorPositions == null || _currentMonitorPositionIndex >= currentAction.MonitorPositions.Count)
            {
                _currentActionIndex++;
                _currentMonitorPositionIndex = 0; // 切换动作后重置监控位置索引
            }

            // 为了避免无限循环导致UI卡死，每次循环后稍微延迟一下
            await Task.Delay(10);
        }
    }

    private async Task ContinuousClick(MonitorPosition monitorPosition, int clickInterval)
    {
        while (monitorPosition.IsClicking)
        {
            int targetX = monitorPosition.X;
            int targetY = monitorPosition.Y;

            // 如果窗口已锁定，将相对坐标转换为屏幕绝对坐标
            if (_lockedWindowHandle != IntPtr.Zero)
            {
                RECT windowRect;
                if (GetWindowRect(_lockedWindowHandle, out windowRect))
                {
                    targetX = windowRect.Left + monitorPosition.X;
                    targetY = windowRect.Top + monitorPosition.Y;
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log("无法获取锁定窗口的矩形信息，将使用原始坐标。");
                    });
                }
            }
            MouseClicker.DoLeftClick(targetX, targetY);
            await Task.Delay(10); // 点击完成后间隔 10 毫秒
            // 记录点击信息

            await Task.Delay(clickInterval > 0 ? clickInterval : 10); // 使用配置的点击间隔，如果未设置则默认为10毫秒
        }
    }

    private void RunStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_colorMonitorTimer.IsEnabled)
        {
            _colorMonitorTimer.Stop();
            _isMonitoringLoopActive = false; // 停止循环
            Log("监控已停止。");

            // 停止所有正在进行的点击
            if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
            {
                foreach (var action in selectedTask.Actions)
                {
                    if (action.MonitorPositions != null)
                    {
                        foreach (var mp in action.MonitorPositions)
                        {
                            mp.IsClicking = false;
                        }
                    }
                }
            }
        }
        else
        {
            _colorMonitorTimer.Start();
            Log("监控已启动。");
        }
    }



    private void CaptureMonitorPosition_Click(object sender, RoutedEventArgs e)
    {
        if (!(sender is System.Windows.Controls.Button button)) return;
        if (!(button.CommandParameter is ActionInfo actionInfo)) return;

        if (actionInfo == null) return;

        _isCapturing = true;
        _currentActionInfoForCapture = actionInfo;
        _currentActionInfoForCapture.IsEditable = true; // 允许编辑

        // 改变鼠标光标，提示用户进入捕获模式
        this.Cursor = System.Windows.Input.Cursors.Cross;
        Log("请移动鼠标到目标位置并点击以捕获坐标和颜色...");

        // 捕获鼠标输入到当前窗口，这样即使鼠标移出窗口，事件也会被捕获
        Mouse.Capture(this);
    }

    private void MainWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isCapturing && _currentActionInfoForCapture is not null)
        {
            ActionInfo? actionInfo = _currentActionInfoForCapture; // 确保 actionInfo 在此上下文中是非空的

            if (actionInfo == null) return;

            // 释放鼠标捕获
            Mouse.Capture(null);

            // 恢复默认光标
            this.Cursor = System.Windows.Input.Cursors.Arrow;

            // 获取当前鼠标位置
            System.Drawing.Point currentMousePosition = System.Windows.Forms.Cursor.Position;

            int x = currentMousePosition.X;
            int y = currentMousePosition.Y;

            // 如果窗口已锁定，计算相对坐标
            if (_lockedWindowHandle != IntPtr.Zero)
            {
                RECT windowRect;
                if (GetWindowRect(_lockedWindowHandle, out windowRect))
                {
                    x = currentMousePosition.X - windowRect.Left;
                    y = currentMousePosition.Y - windowRect.Top;
                    Log($"在锁定窗口中捕获到相对坐标: X={x}, Y={y}");
                }
                else
                {
                    Log("无法获取锁定窗口的矩形信息。");
                }
            }
            else
            {
                Log($"捕获到屏幕坐标: X={x}, Y={y}");
            }

            // 获取该位置的颜色
            System.Windows.Media.Color color = ScreenCapture.GetPixelColor(currentMousePosition.X, currentMousePosition.Y);

            // 更新 ActionInfo
            var monitorPosition = new MonitorPosition
            {
                X = x,
                Y = y,
                ColorHex = color.ToString(),
                ExpectedColor = color
            };

            actionInfo.MonitorPositions.Add(monitorPosition);

            // 捕获完成后重置状态
            _isCapturing = false;
            if (_currentActionInfoForCapture != null)
            {
                _currentActionInfoForCapture.IsEditable = false; // 捕获完成后设置为不可编辑
            }


            Log("捕获完成。");

            // 刷新 DataGrid 以显示新捕获的位置
            // 检查并提交任何正在进行的 AddNew 或 EditItem 事务
            var collectionView = CollectionViewSource.GetDefaultView(ActionsDataGrid.ItemsSource) as IEditableCollectionView;
            if (collectionView != null)
            {
                if (collectionView.IsAddingNew)
                {
                    collectionView.CommitNew();
                }
                if (collectionView.IsEditingItem)
                {
                    collectionView.CommitEdit();
                }
            }
            ActionsDataGrid.Items.Refresh();

            if (actionInfo != null)
            {
                actionInfo.MonitorX = x;
                actionInfo.MonitorY = y;
                actionInfo.MonitorColor = color.ToString();
            }

            // 刷新 DataGrid 显示
            ActionsDataGrid.Items.Refresh();

            // 捕获完成后，将 ActionInfo 设置为不可编辑
            actionInfo!.IsEditable = false;

            _isCapturing = false;
            _currentActionInfoForCapture = null;
            Log("坐标和颜色已捕获。");

            // 捕获完成后，自动新增一行以便新增动作
            if (TaskComboBox.SelectedItem is TaskInfo selectedTask)
            {
                ActionInfo newAction = new ActionInfo { ActionName = string.Empty }; // 新增一个空的动作
                selectedTask.Actions.Add(newAction);
                CurrentActions.Add(newAction);
                Log("已自动新增一行动作。");
            }
        }
    }
}

    public class ProcessInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }

        public ProcessInfo()
        {
            Title = string.Empty;
            ProcessName = string.Empty;
        }

        public override string ToString()
        {
            return Title;
        }
    }

    public class TaskInfo
    {
        public string TaskName { get; set; }
        public List<ActionInfo> Actions { get; set; }

        public TaskInfo()
        {
            TaskName = string.Empty;
            Actions = new List<ActionInfo>();
        }

        public override string ToString()
        {
            return TaskName;
        }
    }

    public class ActionInfo : INotifyPropertyChanged
{
    private string _actionName = string.Empty;
    public string ActionName
    {
        get => _actionName;
        set
        {
            if (_actionName != value)
            {
                _actionName = value;
                OnPropertyChanged(nameof(ActionName));
            }
        }
    }

    private ObservableCollection<MonitorPosition> _monitorPositions = new ObservableCollection<MonitorPosition>();
    public ObservableCollection<MonitorPosition> MonitorPositions
    {
        get => _monitorPositions;
        set
        {
            if (_monitorPositions != value)
            {
                _monitorPositions = value;
                OnPropertyChanged(nameof(MonitorPositions));
            }        }
    }

    public string FormattedMonitorPositions
    {
        get
        {
            if (MonitorPositions == null || MonitorPositions.Count == 0)
            {
                return "";
            }
            return string.Join("; ", MonitorPositions.Select(p => p.DisplayString));
        }
    }

    private int _clickInterval = 100; // 默认点击间隔100毫秒
    public int ClickInterval
    {
        get => _clickInterval;
        set
        {
            if (_clickInterval != value)
            {
                _clickInterval = value;
                OnPropertyChanged(nameof(ClickInterval));
            }
        }
    }

    private bool _isEditable = true; // 是否可编辑，用于控制捕获按钮的可见性
    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (_isEditable != value)
            {
                _isEditable = value;
                OnPropertyChanged(nameof(IsEditable));
            }
        }
    }

    // 用于日志记录的额外属性
    public string ActionType { get; set; } = string.Empty;
    public DateTime ActionTime { get; set; }
    public int MonitorX { get; set; }
    public int MonitorY { get; set; }
    public string MonitorColor { get; set; } = string.Empty;

    public string DisplayColor
    {
        get
        {
            if (MonitorPositions != null && MonitorPositions.Any())
            {
                return MonitorPositions.First().ColorHex;
            }
            return "#FFFFFF"; // Default to white if no monitor positions
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

    public class MonitorPosition : INotifyPropertyChanged
    {
        private int _x;
        public int X
        {
            get { return _x; }
            set
            {
                if (_x != value)
                {
                    _x = value;
                    OnPropertyChanged(nameof(X));
                    OnPropertyChanged(nameof(DisplayString));
                }
            }
        }

        private int _y;
        public int Y
        {
            get { return _y; }
            set
            {
                if (_y != value)
                {
                    _y = value;
                    OnPropertyChanged(nameof(Y));
                    OnPropertyChanged(nameof(DisplayString));
                }
            }
        }

        private string _colorHex = string.Empty;
        public string ColorHex
        {
            get { return _colorHex; }
            set
            {
                if (_colorHex != value)
                {
                    _colorHex = value;
                    OnPropertyChanged(nameof(ColorHex));
                    OnPropertyChanged(nameof(DisplayString));
                }
            }
        }

        private System.Windows.Media.Color? _expectedColor;
        public System.Windows.Media.Color? ExpectedColor
        {
            get { return _expectedColor; }
            set
            {
                if (_expectedColor != value)
                {
                    _expectedColor = value;
                    OnPropertyChanged(nameof(ExpectedColor));
                }
            }
        }

        private bool _isClicking;
        public bool IsClicking
        {
            get { return _isClicking; }
            set
            {
                if (_isClicking != value)
                {
                    _isClicking = value;
                    OnPropertyChanged(nameof(IsClicking));
                }
            }
        }

        public string DisplayString => $"X:{X}, Y:{Y}, Color:{ColorHex}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayString;
        }
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string hexColor)
            {
                if (string.IsNullOrEmpty(hexColor))
                {
                    return System.Windows.Media.Brushes.Transparent; // Return transparent for empty or null hexColor
                }
                try
                {
                    var brush = new System.Windows.Media.BrushConverter().ConvertFromString(hexColor) as System.Windows.Media.SolidColorBrush;
                    return brush ?? System.Windows.Media.Brushes.Transparent; // Return transparent if conversion result is null
                }
                catch
                {
                    // Fallback to a default color if conversion fails
                    return System.Windows.Media.Brushes.Transparent;
                }
            }
            return System.Windows.Media.Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }