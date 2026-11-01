using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Naqsh.App.Monitors;
using Naqsh.App.Services;
using Naqsh.Core.Diagnostics;
using Naqsh.Core.Formats;
using Naqsh.Core.Ipc;
using Naqsh.Core.Model;
using Naqsh.Core.Settings;
using Naqsh.Localization;

namespace Naqsh.App.ViewModels;

/// <summary>Main window: switcher, inputs, recording and status.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Colour presets for "Add colour": resource key suffix and sRGB value.</summary>
    public static IReadOnlyList<ColorPreset> ColorPresets { get; } =
    [
        new("Black", RgbaColor.FromBytes(0, 0, 0)),
        new("White", RgbaColor.FromBytes(255, 255, 255)),
        new("Lapis", RgbaColor.FromBytes(0x26, 0x4F, 0x9C)),
        new("Turquoise", RgbaColor.FromBytes(0x2B, 0xB3, 0xA6)),
        new("Saffron", RgbaColor.FromBytes(0xE0, 0xA9, 0x3B)),
        new("Red", RgbaColor.FromBytes(0xC8, 0x32, 0x3F)),
        new("Green", RgbaColor.FromBytes(0x2E, 0x8A, 0x55)),
        new("Blue", RgbaColor.FromBytes(0x1E, 0x4F, 0xA8)),
        new("Gray", RgbaColor.FromBytes(0x80, 0x80, 0x80)),
    ];

    public static IReadOnlyList<int> FadeDurations { get; } = [500, 1000, 1500, 2000];

    private readonly EngineClient _engine;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _notificationTimer;
    private StateEvent? _state;
    private StatsEvent? _stats;
    private HelloResponse? _hello;
    private string? _notificationKey;
    private string? _notificationArg;

    [ObservableProperty]
    private MonitorSource? _previewSource;

    [ObservableProperty]
    private MonitorSource? _programSource;

    [ObservableProperty]
    private EngineConnection _connection = EngineConnection.Connecting;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isProMode = true;

    [ObservableProperty]
    private int _fadeDurationMs;

    [ObservableProperty]
    private string _clockText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _recordingTime = string.Empty;

    public MainViewModel(EngineClient engine, SettingsService settings, IDialogService dialogs, Dispatcher dispatcher)
    {
        _engine = engine;
        _settings = settings;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _fadeDurationMs = settings.Current.FadeDurationMs;
        _engine.MessageReceived += msg => _dispatcher.InvokeAsync(() => OnMessage(msg));
        _engine.ConnectionChanged += c => _dispatcher.InvokeAsync(() => Connection = c);
        LocalizationService.Instance.LanguageChanged += (_, _) => RefreshText();

        _clockTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _clockTimer.Start();
        _notificationTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => ClearNotification(), dispatcher);
        Tick();
    }

    public ObservableCollection<InputTileViewModel> Inputs { get; } = [];

    private static LocalizationService L => LocalizationService.Instance;

    public bool HasNoInputs => Inputs.Count == 0;

    public bool IsConnected => Connection == EngineConnection.Connected;

    public bool IsPersian => _settings.Current.Language == AppSettings.Persian;

    public string ConnectionText => L.Get(Connection switch
    {
        EngineConnection.Connected => "Status_Engine_Connected",
        EngineConnection.Lost => "Status_Engine_Lost",
        _ => "Status_Engine_Connecting",
    });

    public string FpsText => _stats is null ? string.Empty : L.Format("Status_Fps", Math.Round(_stats.Fps, 2).ToString("0.00", CultureInfo.InvariantCulture));

    public string DroppedText => L.Format("Status_Dropped", _stats?.DroppedFrames ?? 0);

    public string RecordingDroppedText => _stats is { RecordingDroppedFrames: > 0 } s ? L.Format("Status_RecordingDropped", s.RecordingDroppedFrames) : string.Empty;

    public string FormatText => _state is null ? string.Empty : L.Format("Status_Format", _state.Format.ShortName);

    public string GpuText => _hello is null ? string.Empty : L.Format("Status_Gpu", _hello.AdapterName);

    public string TimecodeText => _stats?.Timecode ?? "--:--:--:--";

    public string? Notification => _notificationKey is null
        ? null
        : _notificationArg is null ? L.Get(_notificationKey) : L.Format(_notificationKey, _notificationArg);

    public string PreviewName => NameOf(_state?.Preview);

    public string ProgramName => NameOf(_state?.Program);

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private Task Cut() => _engine.SendAsync(new CutRequest());

    [RelayCommand]
    private Task Fade() => _engine.SendAsync(new FadeRequest(FadeDurationMs));

    [RelayCommand]
    private void SetFadeDuration(int ms)
    {
        FadeDurationMs = ms;
        _settings.Update(s => s with { FadeDurationMs = ms });
    }

    [RelayCommand]
    private Task AddColor(ColorPreset? preset)
    {
        preset ??= ColorPresets[0];
        var d = new InputDescriptor { Kind = InputKind.Color, Color = preset.Color, Name = L.Get("Color_" + preset.Key) };
        return _engine.SendAsync(new AddInputRequest(d));
    }

    [RelayCommand]
    private Task AddImage() => AddFile(InputKind.Image, "Dialog_OpenImage", "Filter_Images", "*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif");

    [RelayCommand]
    private Task AddVideo() => AddFile(InputKind.Video, "Dialog_OpenVideo", "Filter_Videos", "*.mp4;*.mov;*.mkv;*.avi;*.mxf;*.wmv;*.mpg;*.mpeg;*.m2t;*.ts;*.flv;*.dv");

    [RelayCommand]
    private Task Preview(InputTileViewModel? tile) => tile is null ? Task.CompletedTask : _engine.SendAsync(new SetPreviewRequest(tile.Id));

    [RelayCommand]
    private Task Direct(InputTileViewModel? tile) => tile is null ? Task.CompletedTask : _engine.SendAsync(new DirectRequest(tile.Id));

    [RelayCommand]
    private Task Remove(InputTileViewModel? tile) => tile is null ? Task.CompletedTask : _engine.SendAsync(new RemoveInputRequest(tile.Id));

    [RelayCommand]
    private Task PreviewNumber(string? number)
    {
        if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= Inputs.Count)
        {
            return _engine.SendAsync(new SetPreviewRequest(Inputs[n - 1].Id));
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ToggleRecord()
    {
        if (IsRecording)
        {
            return _engine.SendAsync(new StopRecordingRequest());
        }

        var folder = _settings.Current.RecordingFolder ?? NaqshPaths.DefaultRecordings;
        return _engine.SendAsync(new StartRecordingRequest(folder));
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = L.IsRightToLeft ? AppSettings.English : AppSettings.Persian;
        _settings.Update(s => s with { Language = next });
    }

    [RelayCommand]
    private void ToggleMode() => IsProMode = !IsProMode;

    /// <summary>Sends a project format change (refused by the engine while on air).</summary>
    public Task SetFormatAsync(VideoFormat format) => _engine.SendAsync(new SetFormatRequest(format));

    public VideoFormat? CurrentFormat => _state?.Format;

    /// <summary>Called by the window when the user closes it.</summary>
    public async Task ShutdownAsync()
    {
        if (IsRecording)
        {
            await _engine.SendAsync(new StopRecordingRequest()).ConfigureAwait(true);
        }

        await _engine.GoodbyeAsync(shutdownEngine: true).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ engine messages

    private void OnMessage(IpcMessage msg)
    {
        switch (msg)
        {
            case HelloResponse h:
                _hello = h;
                PreviewSource = new MonitorSource(h.PreviewSharedName, h.AdapterLuid, h.IsWarp, h.MonitorWidth, h.MonitorHeight);
                ProgramSource = new MonitorSource(h.ProgramSharedName, h.AdapterLuid, h.IsWarp, h.MonitorWidth, h.MonitorHeight);
                OnPropertyChanged(nameof(GpuText));
                break;
            case StateEvent s:
                ApplyState(s);
                break;
            case StatsEvent st:
                _stats = st;
                OnPropertyChanged(nameof(FpsText));
                OnPropertyChanged(nameof(DroppedText));
                OnPropertyChanged(nameof(RecordingDroppedText));
                OnPropertyChanged(nameof(TimecodeText));
                break;
            case ErrorEvent e:
                Notify("Error_" + e.Code, null);
                break;
        }
    }

    private void ApplyState(StateEvent s)
    {
        var wasRecording = IsRecording;
        _state = s;
        IsRecording = s.Recording.Active;
        if (wasRecording && !IsRecording)
        {
            Notify("Record_Saved", s.Recording.FilePath ?? string.Empty);
        }

        // Sync tiles in engine order without recreating unchanged ones.
        for (var i = 0; i < s.Inputs.Count; i++)
        {
            var input = s.Inputs[i];
            var existing = Inputs.FirstOrDefault(t => t.Id == input.Input.Id);
            if (existing is null)
            {
                existing = new InputTileViewModel(input.Input);
                Inputs.Insert(Math.Min(i, Inputs.Count), existing);
            }
            else
            {
                var idx = Inputs.IndexOf(existing);
                if (idx != i)
                {
                    Inputs.Move(idx, Math.Min(i, Inputs.Count - 1));
                }

                existing.Descriptor = input.Input;
            }

            existing.Number = i + 1;
            existing.Status = input.Status;
            existing.IsOnProgram = input.Input.Id == s.Program;
            existing.IsOnPreview = input.Input.Id == s.Preview;
        }

        for (var i = Inputs.Count - 1; i >= 0; i--)
        {
            if (s.Inputs.All(x => x.Input.Id != Inputs[i].Id))
            {
                Inputs.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(HasNoInputs));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(PreviewName));
        OnPropertyChanged(nameof(ProgramName));
        OnPropertyChanged(nameof(CurrentFormat));
    }

    private async Task AddFile(InputKind kind, string titleKey, string filterKey, string patterns)
    {
        var path = _dialogs.OpenFile(titleKey, filterKey, patterns);
        if (path is null)
        {
            return;
        }

        var d = new InputDescriptor { Kind = kind, Path = path, Name = Path.GetFileNameWithoutExtension(path) };
        await _engine.SendAsync(new AddInputRequest(d)).ConfigureAwait(true);
    }

    private string NameOf(Guid? id)
    {
        if (id is null || id == Guid.Empty)
        {
            return string.Empty;
        }

        return Inputs.FirstOrDefault(t => t.Id == id)?.Name ?? string.Empty;
    }

    private void Notify(string key, string? arg)
    {
        _notificationKey = key;
        _notificationArg = arg;
        OnPropertyChanged(nameof(Notification));
        _notificationTimer.Stop();
        _notificationTimer.Start();
    }

    private void ClearNotification()
    {
        _notificationTimer.Stop();
        _notificationKey = null;
        OnPropertyChanged(nameof(Notification));
    }

    private void Tick()
    {
        var now = DateTime.Now;
        ClockText = DateFormatter.FormatTime(now, L);
        DateText = DateFormatter.FormatDate(now, L);
        if (_state?.Recording is { Active: true, StartedUtc: { } started })
        {
            var t = DateTimeOffset.UtcNow - started;
            RecordingTime = L.Digits(t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        }
        else
        {
            RecordingTime = string.Empty;
        }
    }

    private void RefreshText()
    {
        foreach (var t in Inputs)
        {
            t.RefreshText();
        }

        OnPropertyChanged(string.Empty);
        Tick();
    }

    partial void OnConnectionChanged(EngineConnection value)
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnected));
    }
}

/// <summary>A colour preset; <see cref="Key"/> is the resource key suffix (Color_Key).</summary>
public sealed record ColorPreset(string Key, RgbaColor Color)
{
    public string DisplayName => LocalizationService.Instance.Get("Color_" + Key);

    public System.Windows.Media.Brush Brush
    {
        get
        {
            var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
                (byte)Math.Round(Color.R * 255),
                (byte)Math.Round(Color.G * 255),
                (byte)Math.Round(Color.B * 255)));
            b.Freeze();
            return b;
        }
    }
}
