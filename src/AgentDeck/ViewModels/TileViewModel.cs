using System.Collections.ObjectModel;
using Avalonia.Threading;
using AgentDeck.Models;
using AgentDeck.Settings;
using AgentDeck.Status;
using AgentDeck.Terminal;

namespace AgentDeck.ViewModels;

/// <summary>
/// Один тайл дека: плейсхолдер с выбором директории и утилиты либо живой терминал.
/// </summary>
public sealed class TileViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>
    /// Заголовок тайла, у которого директория не задана.
    /// </summary>
    public const string UntitledName = "New agent";

    private readonly CommandResolver _commandResolver;
    private readonly ObservableCollection<LaunchOptionViewModel> _launchOptions = [];

    private string _directory;
    private AgentKind? _agentKind;
    private string? _utilityName;
    private LaunchOptionViewModel? _launched;
    private TileStatus _status = TileStatus.Placeholder;
    private string? _error;
    private TerminalHost? _terminal;

    /// <summary>
    /// Создаёт тайл с указанным идентификатором и стартовой директорией.
    /// </summary>
    /// <param name="id">Идентификатор тайла.</param>
    /// <param name="directory">Стартовая рабочая директория.</param>
    /// <param name="commandResolver">Проверка команд перед запуском; по умолчанию системная.</param>
    public TileViewModel(Guid id, string directory, CommandResolver? commandResolver = null)
    {
        Id = id;
        _directory = directory ?? string.Empty;
        _commandResolver = commandResolver ?? new CommandResolver();
        LaunchOptions = new ReadOnlyObservableCollection<LaunchOptionViewModel>(_launchOptions);
    }

    /// <summary>
    /// Пользователь нажал ✕.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Пользователь выбрал утилиту для запуска.
    /// </summary>
    public event EventHandler<LaunchOptionViewModel>? LaunchRequested;

    /// <summary>
    /// Пользователь нажал ↻ для перезапуска упавшего или завершённого процесса.
    /// </summary>
    public event EventHandler? RestartRequested;

    /// <summary>
    /// Пользователь просит открыть настройки — команда утилиты не найдена.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Идентификатор тайла; совпадает с идентификатором листа раскладки.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Варианты запуска, показываемые на плейсхолдере.
    /// </summary>
    public ReadOnlyObservableCollection<LaunchOptionViewModel> LaunchOptions { get; }

    /// <summary>
    /// Хост терминала; появляется при первом запуске утилиты в тайле.
    /// </summary>
    public TerminalHost? Terminal
    {
        get => _terminal;
        private set => SetField(ref _terminal, value);
    }

    /// <summary>
    /// Рабочая директория процесса.
    /// </summary>
    public string Directory
    {
        get => _directory;
        set
        {
            if (SetField(ref _directory, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    /// <summary>
    /// Профиль паттернов статуса запущенной утилиты.
    /// </summary>
    public AgentKind? AgentKind
    {
        get => _agentKind;
        set => SetField(ref _agentKind, value);
    }

    /// <summary>
    /// Имя запущенной (или сохранённой с прошлой сессии) утилиты.
    /// </summary>
    public string? UtilityName
    {
        get => _utilityName;
        set => SetField(ref _utilityName, value);
    }

    /// <summary>
    /// Текущее состояние тайла.
    /// </summary>
    public TileStatus Status
    {
        get => _status;
        set
        {
            if (!SetField(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPlaceholder));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsAwaitingInput));
            OnPropertyChanged(nameof(IsAwaitingPermission));
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(IsCrashed));
            OnPropertyChanged(nameof(CanRestart));
        }
    }

    /// <summary>
    /// Процесс работает и выдаёт вывод.
    /// </summary>
    public bool IsRunning => Status == TileStatus.Running;

    /// <summary>
    /// Агент притих и ждёт ввода.
    /// </summary>
    public bool IsAwaitingInput => Status == TileStatus.AwaitingInput;

    /// <summary>
    /// Агент запросил подтверждение.
    /// </summary>
    public bool IsAwaitingPermission => Status == TileStatus.AwaitingPermission;

    /// <summary>
    /// Процесс завершился штатно.
    /// </summary>
    public bool IsFinished => Status == TileStatus.Finished;

    /// <summary>
    /// Процесс упал.
    /// </summary>
    public bool IsCrashed => Status == TileStatus.Crashed;

    /// <summary>
    /// Сообщение об ошибке запуска, показываемое на плейсхолдере.
    /// </summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (SetField(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// Признак наличия ошибки запуска.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>
    /// Процесс ещё не запущен — показывается плейсхолдер.
    /// </summary>
    public bool IsPlaceholder => Status == TileStatus.Placeholder;

    /// <summary>
    /// Кнопка ↻ видна только для завершённого или упавшего процесса.
    /// </summary>
    public bool CanRestart => Status is TileStatus.Finished or TileStatus.Crashed;

    /// <summary>
    /// Имя проекта — последний сегмент директории.
    /// </summary>
    public string Title => DeriveTitle(Directory);

    /// <summary>
    /// Выводит имя проекта из пути: последний непустой сегмент, устойчиво к
    /// хвостовому разделителю, корню и разделителям обеих платформ.
    /// </summary>
    public static string DeriveTitle(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return UntitledName;
        }

        var trimmed = directory.Trim();
        var segments = trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        // Корень («/», «C:\») собственного имени не имеет — показываем путь как есть.
        return segments.Length == 0 ? trimmed : segments[^1];
    }

    /// <summary>
    /// Пересобирает кнопки запуска по текущему списку утилит, сохраняя подсказку.
    /// </summary>
    public void SetLaunchOptions(IEnumerable<UtilityState> utilities)
    {
        _launchOptions.Clear();

        foreach (var utility in utilities)
        {
            _launchOptions.Add(new LaunchOptionViewModel(utility));
        }

        SuggestAgent(UtilityName);
    }

    /// <summary>
    /// Помечает кнопку утилиты, сохранённой с прошлой сессии.
    /// </summary>
    public void SuggestAgent(string? utilityName)
    {
        UtilityName = utilityName;

        foreach (var option in _launchOptions)
        {
            option.IsSuggested = !string.IsNullOrEmpty(utilityName)
                && string.Equals(option.Name, utilityName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Поднимает запрос на закрытие тайла.
    /// </summary>
    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Поднимает запрос на запуск указанной утилиты.
    /// </summary>
    public void RequestLaunch(LaunchOptionViewModel option) => LaunchRequested?.Invoke(this, option);

    /// <summary>
    /// Поднимает запрос на перезапуск процесса.
    /// </summary>
    public void RequestRestart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Поднимает запрос на открытие настроек.
    /// </summary>
    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Обрабатывает завершение процесса тайла.
    /// </summary>
    /// <param name="exitCode">Код возврата процесса.</param>
    public void NotifyProcessExited(int exitCode)
    {
        Status = exitCode == 0 ? TileStatus.Finished : TileStatus.Crashed;

        // Штатный выход из агента («/exit», Ctrl+D, конец скрипта) закрывает
        // тайл: терминал мёртвого процесса ввода уже не принимает и выглядит
        // зависшим. Упавший процесс тайл сохраняет — иначе его вывод исчезнет
        // с экрана вместе с причиной падения, и перезапускать будет нечего.
        if (exitCode == 0)
        {
            RequestClose();
        }
    }

    /// <summary>
    /// Запускает утилиту в тайле: проверяет директорию и команду, поднимает PTY
    /// и подменяет плейсхолдер терминалом. Ошибка запуска остаётся на плейсхолдере.
    /// </summary>
    public async Task LaunchAsync(LaunchOptionViewModel option)
    {
        var directory = ExpandDirectory(Directory);

        if (!System.IO.Directory.Exists(directory))
        {
            Error = $"Directory not found: {directory}";
            return;
        }

        var profile = AgentLaunchProfile.Create(option.Kind, option.Command, directory);

        // Ненайденную команду ловим до запуска: иначе shell напишет
        // «command not found» внутрь терминала, а тайл молча станет упавшим.
        // Проверка поднимает login-интерактивный shell — это сотни миллисекунд
        // на тяжёлом ~/.bashrc, поэтому уводим её с UI-потока.
        var missing = await Task
            .Run(() => _commandResolver.FindMissingCommand(
                option.Command,
                profile.Environment,
                profile.WorkingDirectory))
            .ConfigureAwait(true);

        if (missing is not null)
        {
            Error = $"{missing}: command not found — set the full path in Settings.";
            return;
        }

        Error = null;
        Directory = directory;
        AgentKind = option.Kind;
        UtilityName = option.Name;
        _launched = option;

        var host = Terminal ??= CreateHost();
        var previousStatus = Status;

        // Терминал показывается до старта процесса: пока контрол не разложен,
        // хост не знает реальных cols/rows и PTY стартовал бы с 80×24 —
        // первый вывод свернулся бы не по той ширине.
        Status = TileStatus.Running;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

        try
        {
            await host.StartAsync(profile).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Error = $"Failed to launch {DescribeLaunch(option)}: {exception.Message}";
            Status = previousStatus == TileStatus.Running ? TileStatus.Crashed : TileStatus.Placeholder;
        }
    }

    /// <summary>
    /// Перезапускает ту же утилиту в том же тайле с чистым буфером.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_launched is not { } option)
        {
            return;
        }

        Terminal?.Reset();
        await LaunchAsync(option).ConfigureAwait(true);
    }

    /// <summary>
    /// Гасит процесс тайла и освобождает PTY.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Terminal is not { } terminal)
        {
            return;
        }

        Terminal = null;
        terminal.Exited -= OnTerminalExited;
        await terminal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Раскрывает «~» в домашнюю директорию и убирает лишние пробелы.
    /// </summary>
    internal static string ExpandDirectory(string? directory) => PathUtilities.ExpandHome(directory);

    /// <summary>
    /// Подпись утилиты для сообщения об ошибке: имя, а для безымянной — команда.
    /// </summary>
    private static string DescribeLaunch(LaunchOptionViewModel option)
        => option.Name.Length > 0 ? option.Name : option.Command;

    private TerminalHost CreateHost()
    {
        var host = new TerminalHost();
        host.Exited += OnTerminalExited;
        return host;
    }

    private void OnTerminalExited(object? sender, int exitCode) => NotifyProcessExited(exitCode);
}
