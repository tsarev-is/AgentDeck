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

    /// <summary>
    /// Пауза перед чтением каталога. Путь в поле правят посимвольно, и без
    /// паузы каждая буква уходила бы в файловую систему.
    /// </summary>
    public static readonly TimeSpan DefaultBrowseDelay = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Сколько чипов вложенных папок видно сразу. В каталоге вроде «/usr» их
    /// сотни: длинная полоса выдавливает из тайла кнопки запуска, а список
    /// папок ещё и не виртуализирован — кнопка создаётся на каждую папку.
    /// </summary>
    public const int DefaultFolderCap = 8;

    /// <summary>
    /// На сколько чипов вырастает полоса по нажатию «… N more».
    /// </summary>
    public const int FolderCapStep = 16;

    private readonly CommandResolver _commandResolver;
    private readonly DirectoryBrowser _browser;
    private readonly TimeSpan _browseDelay;
    private readonly ObservableCollection<LaunchOptionViewModel> _launchOptions = [];

    private string _directory;
    private AgentKind? _agentKind;
    private string? _utilityName;
    private LaunchOptionViewModel? _launched;
    private TileStatus _status = TileStatus.Placeholder;
    private string? _error;
    private TerminalHost? _terminal;
    private string _filter = string.Empty;

    /// <summary>
    /// Последний прочитанный каталог; null — не читали ни разу.
    /// </summary>
    private DirectoryListing? _listing;

    /// <summary>
    /// Путь, к которому относится <see cref="_listing"/>. Пути чипов строятся от
    /// него, а не от текущей директории: пока новый каталог читается, на экране
    /// остаются чипы прежнего.
    /// </summary>
    private string _listedDirectory = string.Empty;

    private IReadOnlyList<FolderEntryViewModel> _folders = [];
    private IReadOnlyList<object> _chips = [];
    private MoreFoldersViewModel? _more;
    private int _folderCap = DefaultFolderCap;
    private CancellationTokenSource? _browseCts;

    /// <summary>
    /// Создаёт тайл с указанным идентификатором и стартовой директорией.
    /// </summary>
    /// <param name="id">
    /// Идентификатор тайла.
    /// </param>
    /// <param name="directory">
    /// Стартовая рабочая директория.
    /// </param>
    /// <param name="commandResolver">
    /// Проверка команд перед запуском; по умолчанию системная.
    /// </param>
    /// <param name="browser">
    /// Чтение вложенных директорий; по умолчанию системное.
    /// </param>
    /// <param name="browseDelay">
    /// Пауза перед чтением каталога после правки пути; по умолчанию
    /// <see cref="DefaultBrowseDelay"/>.
    /// </param>
    public TileViewModel(
        Guid id,
        string directory,
        CommandResolver? commandResolver = null,
        DirectoryBrowser? browser = null,
        TimeSpan? browseDelay = null)
    {
        Id = id;
        _directory = directory ?? string.Empty;
        _commandResolver = commandResolver ?? new CommandResolver();
        _browser = browser ?? new DirectoryBrowser();
        _browseDelay = browseDelay ?? DefaultBrowseDelay;
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

        // Путь из поля правят посимвольно — такой правке положена пауза.
        set => SetDirectory(value, _browseDelay);
    }

    /// <summary>
    /// Текст поиска по вложенным папкам. Каждый символ сужает список чипов,
    /// не перечитывая каталог.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Вложенные папки текущей директории, прошедшие фильтр и попавшие в
    /// показанную порцию.
    /// </summary>
    public IReadOnlyList<FolderEntryViewModel> Folders => _folders;

    /// <summary>
    /// Полоса чипов для представления: папки, а за ними — чип «… N more», если
    /// фильтру есть что скрывать. Разнотипность разбирают шаблоны по типу чипа.
    /// </summary>
    public IReadOnlyList<object> FolderChips => _chips;

    /// <summary>
    /// Чип «показать ещё» или null, когда скрывать нечего.
    /// </summary>
    public MoreFoldersViewModel? MoreFolders => _more;

    /// <summary>
    /// В списке есть хотя бы одна папка.
    /// </summary>
    public bool HasFolders => _folders.Count > 0;

    /// <summary>
    /// Подсказка вместо пустого списка папок. Пока каталог не прочитан, подсказки
    /// нет: «не найдено» о непрочитанном каталоге — неправда, а на медленной
    /// файловой системе это окно видно глазами.
    /// </summary>
    public string FolderHint
    {
        get
        {
            if (_listing is not { } listing)
            {
                return string.Empty;
            }

            if (!listing.Exists)
            {
                return "directory not found";
            }

            return _filter.Trim().Length > 0 ? "no matching folders" : "no folders inside";
        }
    }

    /// <summary>
    /// Подсказку показываем вместо полосы папок, только когда есть что сказать.
    /// </summary>
    public bool HasFolderHint => !HasFolders && FolderHint.Length > 0;

    /// <summary>
    /// Из текущей директории есть куда подняться — она не корень.
    /// </summary>
    public bool CanGoUp => PathUtilities.Parent(Directory) is not null;

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

            // Тайлу с живым терминалом выбирать папку уже не нужно — незачем и
            // держать наготове список из файловой системы. Возврат в плейсхолдер
            // (сорвавшийся запуск) список восстанавливает сам: представление
            // висит в дереве постоянно и второй раз BeginBrowse не позовёт.
            if (IsPlaceholder)
            {
                ScheduleBrowse(TimeSpan.Zero);
            }
            else
            {
                StopBrowsing();
            }
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
    /// Начинает следить за вложенными папками текущей директории. Вызывается
    /// представлением при показе плейсхолдера: тайлу, который так и не показали,
    /// файловая система не нужна.
    /// </summary>
    public void BeginBrowse() => ScheduleBrowse(TimeSpan.Zero);

    /// <summary>
    /// Текущее чтение каталога: пока задача не завершилась, список папок ещё
    /// не догнал путь. Плейсхолдеру задача не нужна — он обновляется по
    /// уведомлениям, — но проверкам выбора папки нужна точка ожидания.
    /// </summary>
    public Task BrowseTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Поднимает рабочую директорию на уровень выше.
    /// </summary>
    /// <returns>
    /// false, если подниматься уже некуда.
    /// </returns>
    public bool GoUp()
    {
        if (PathUtilities.Parent(Directory) is not { } parent)
        {
            return false;
        }

        // Переход по кнопке — не набор пути по буквам, ждать паузу незачем.
        SetDirectory(parent, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Входит во вложенную папку.
    /// </summary>
    public void EnterFolder(FolderEntryViewModel folder)
        // Переход идёт по собственному пути чипа, а не по имени поверх текущей
        // директории: пока читается новый каталог, на экране остаются чипы
        // прежнего, и клик обязан вести туда, куда обещает подсказка.
        => SetDirectory(folder.FullPath, TimeSpan.Zero);

    /// <summary>
    /// Показывает следующую порцию папок — нажатие на чип «… N more». Каталог
    /// при этом не перечитывается: полоса пересобирается из того же листинга.
    /// </summary>
    public void ShowMoreFolders()
    {
        if (_more is null)
        {
            return;
        }

        _folderCap += FolderCapStep;
        ApplyFilter();
    }

    /// <summary>
    /// Входит в первую папку из отфильтрованного списка — Enter в поле поиска.
    /// </summary>
    /// <returns>
    /// false, если список пуст.
    /// </returns>
    public bool EnterFirstFolder()
    {
        if (_folders.FirstOrDefault() is not { } folder)
        {
            return false;
        }

        EnterFolder(folder);
        return true;
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
    /// <param name="exitCode">
    /// Код возврата процесса.
    /// </param>
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

        // В заголовке остаётся короткий вид пути: раскрытый «~» съедал бы место
        // ради того, что и так известно.
        Directory = PathUtilities.CollapseHome(directory);
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
        StopBrowsing();

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
    private static string ExpandDirectory(string? directory) => PathUtilities.ExpandHome(directory);

    /// <summary>
    /// Меняет рабочую директорию и планирует чтение нового каталога. Пауза —
    /// параметр, потому что она нужна не всякой смене пути: набор по буквам её
    /// ждёт, переход по кнопке или чипу — нет.
    /// </summary>
    /// <param name="path">
    /// Новая рабочая директория.
    /// </param>
    /// <param name="delay">
    /// Пауза перед чтением каталога.
    /// </param>
    private void SetDirectory(string? path, TimeSpan delay)
    {
        if (!SetField(ref _directory, path ?? string.Empty, nameof(Directory)))
        {
            return;
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanGoUp));

        // Раскрытая порция относится к прежнему каталогу так же, как фильтр:
        // сколько его папок показали, о новом каталоге не говорит ничего.
        // Сброс идёт до правки фильтра — та пересобирает полосу на месте.
        _folderCap = DefaultFolderCap;

        // Фильтр относится к прежнему каталогу — в новом он бессмыслен.
        Filter = string.Empty;
        ScheduleBrowse(delay);
    }

    /// <summary>
    /// Планирует чтение каталога, отменяя предыдущее. Пауза схлопывает набор
    /// пути по буквам в одно обращение к файловой системе.
    /// </summary>
    private void ScheduleBrowse(TimeSpan delay)
    {
        if (!IsPlaceholder)
        {
            return;
        }

        CancelBrowse();

        var cts = new CancellationTokenSource();
        _browseCts = cts;
        BrowseTask = BrowseAsync(Directory, delay, cts.Token);
    }

    /// <summary>
    /// Читает каталог вне UI-потока и обновляет список папок, если результат
    /// ещё актуален.
    /// </summary>
    private async Task BrowseAsync(string directory, TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token).ConfigureAwait(true);
            }

            var expanded = PathUtilities.ExpandHome(directory);
            var listing = await Task.Run(() => _browser.List(expanded), token).ConfigureAwait(true);

            // Пока каталог читался, путь могли поправить — устаревший список
            // затёр бы актуальный.
            if (token.IsCancellationRequested)
            {
                return;
            }

            _listing = listing;
            _listedDirectory = directory;
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Чтение отменено следующей правкой пути — результат уже не нужен.
        }
    }

    /// <summary>
    /// Пересобирает чипы по последнему прочитанному каталогу и тексту фильтра.
    /// </summary>
    private void ApplyFilter()
    {
        var filter = _filter.Trim();
        var names = _listing?.Folders ?? [];

        // Список пересобирается заново, а не чистится на месте: полоса уходит в
        // представление как единое значение, и подменять его надо целиком.
        var folders = new List<FolderEntryViewModel>();

        // Фильтр идёт по всему прочитанному каталогу, а порция режет уже
        // найденное: иначе папку, не попавшую в показанную часть, поиск бы не
        // находил вовсе — и свёрнутый список стал бы ловушкой.
        var matched = 0;

        foreach (var name in names)
        {
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matched++;

            if (folders.Count < _folderCap)
            {
                // Путь чипа — от прочитанного каталога, а не от текущей
                // директории: та могла уже уехать вперёд, и чип обещал бы
                // соседа по новому каталогу вместо папки, которую показывает.
                folders.Add(new FolderEntryViewModel(name, PathUtilities.Child(_listedDirectory, name)));
            }
        }

        var hidden = matched - folders.Count;
        _more = hidden > 0 ? new MoreFoldersViewModel(hidden) : null;

        var chips = new List<object>(folders);

        if (_more is not null)
        {
            chips.Add(_more);
        }

        _folders = folders;
        _chips = chips;

        NotifyFolderState();
    }

    /// <summary>
    /// Гасит чтение каталога и убирает список папок.
    /// </summary>
    private void StopBrowsing()
    {
        CancelBrowse();

        // Порцию сбрасываем и на пустом списке: тайл может вернуться в
        // плейсхолдер (запуск не удался), и раскрытая полоса переживёт паузу.
        _folderCap = DefaultFolderCap;

        if (_folders.Count == 0 && _listing is null)
        {
            return;
        }

        _folders = [];
        _more = null;
        _chips = [];
        _listing = null;
        _listedDirectory = string.Empty;
        NotifyFolderState();
    }

    private void CancelBrowse()
    {
        _browseCts?.Cancel();
        _browseCts?.Dispose();
        _browseCts = null;
    }

    private void NotifyFolderState()
    {
        OnPropertyChanged(nameof(Folders));
        OnPropertyChanged(nameof(FolderChips));
        OnPropertyChanged(nameof(MoreFolders));
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(FolderHint));
        OnPropertyChanged(nameof(HasFolderHint));
    }

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
