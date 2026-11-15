using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.ChatWatcher.ViewModels;

/// <summary>One person who triggered the watch: their cleaned-up text and the invite buttons.</summary>
public sealed partial class RequestCardViewModel : ObservableObject
{
    private readonly Func<RequestCardViewModel, string, Task> _invite;
    private readonly Action<RequestCardViewModel> _dismiss;

    public RequestCardViewModel(ChatRequest request, Func<RequestCardViewModel, string, Task> invite, Action<RequestCardViewModel> dismiss)
    {
        _invite = invite;
        _dismiss = dismiss;
        Name = request.Name;
        Key = request.Name.Trim().ToLowerInvariant();
        Update(request);
    }

    public string Name { get; }
    public string Key { get; }
    public DateTime LastSeen { get; private set; }

    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _timeText = "";

    public string ChannelText => $"[{Channel}]";

    partial void OnChannelChanged(string value) => OnPropertyChanged(nameof(ChannelText));

    public void Update(ChatRequest request)
    {
        Channel = request.Channel;
        Message = request.Message;
        TimeText = request.Timestamp.ToString("HH:mm:ss");
        LastSeen = DateTime.Now;
    }

    [RelayCommand] private Task Team() => _invite(this, "/invite");
    [RelayCommand] private Task League() => _invite(this, "/league_invite");
    [RelayCommand] private void Dismiss() => _dismiss(this);
}

/// <summary>
/// Watches the newest chat log of the selected installation / account for people matching the keywords and
/// lists them with Team / League invite buttons.
/// </summary>
public sealed partial class ChatWatcherViewModel : ViewModelBase
{
    private const double DuplicateWindowSeconds = 60;

    private readonly AppState _state;
    private readonly LogTailer _tailer = new();
    private readonly DispatcherTimer _timer;
    private ChatWatcherSettings _settings = ChatWatcherSettings.Load();
    private List<Regex> _keywordPatterns = new();
    private bool _loading;
    private bool _dirty;
    private bool _sending;

    public ChatWatcherViewModel(AppState state)
    {
        _state = state;
        LoadFromSettings();

        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                Retarget();
        };
        Retarget();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public ObservableCollection<WatchFilter> Filters { get; } = new();
    public ObservableCollection<RequestCardViewModel> Cards { get; } = new();
    public bool IsEmpty => Cards.Count == 0;
    public bool CanSendDirectly => OperatingSystem.IsWindows();

    [ObservableProperty] private WatchFilter? _selectedFilter;
    [ObservableProperty] private string _keywords = "";
    [ObservableProperty] private bool _watchTell = true;
    [ObservableProperty] private bool _watchLocal = true;
    [ObservableProperty] private bool _watchBroadcast = true;
    [ObservableProperty] private bool _watchRequest;
    [ObservableProperty] private bool _sendDirectly;

    /// <summary>When off the log is not read at all; turning it back on starts from the end of the current log.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorButtonText))]
    private bool _isMonitoring;

    public string MonitorButtonText => IsMonitoring ? "Stop Monitoring" : "Start Monitoring";
    /// <summary>The character that is logged in (from the log's greeting); their own messages are not listed.</summary>
    [ObservableProperty] private string? _currentCharacter;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _resultText = "";

    // ---- settings <-> pane ----

    private void LoadFromSettings()
    {
        _loading = true;
        Filters.Clear();
        foreach (var f in _settings.Filters)
            Filters.Add(f);
        SelectedFilter = Filters.FirstOrDefault(f => f.Name.Equals(_settings.SelectedFilter, StringComparison.OrdinalIgnoreCase));
        Keywords = _settings.Keywords;
        WatchTell = _settings.WatchTell;
        WatchLocal = _settings.WatchLocal;
        WatchBroadcast = _settings.WatchBroadcast;
        WatchRequest = _settings.WatchRequest;
        SendDirectly = _settings.SendDirectly && CanSendDirectly;
        IsMonitoring = false;       // always starts stopped; the last on/off state is deliberately not restored
        _keywordPatterns = ChatLogParser.CompileKeywords(ChatLogParser.SplitKeywords(Keywords));
        _loading = false;
    }

    private void MarkDirty()
    {
        if (!_loading)
            _dirty = true;
    }

    /// <summary>Writes the pane's current state to the ini (done from the timer so typing doesn't write per keystroke).</summary>
    private void FlushSettings()
    {
        if (!_dirty)
            return;
        _dirty = false;
        _settings.SelectedFilter = SelectedFilter?.Name ?? "";
        _settings.Keywords = Keywords;
        _settings.WatchTell = WatchTell;
        _settings.WatchLocal = WatchLocal;
        _settings.WatchBroadcast = WatchBroadcast;
        _settings.WatchRequest = WatchRequest;
        _settings.SendDirectly = SendDirectly;
        _settings.Monitoring = IsMonitoring;
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultText = "Could not write chatwatcher.ini: " + ex.Message;
        }
    }

    partial void OnSelectedFilterChanged(WatchFilter? value)
    {
        MarkDirty();
        if (_loading || value is null)
            return;

        // Quick-select: load the filter into the keyword box and channel checkboxes
        _loading = true;
        Keywords = value.Keywords;
        WatchTell = value.Tell;
        WatchLocal = value.Local;
        WatchBroadcast = value.Broadcast;
        WatchRequest = value.Request;
        _loading = false;
        _keywordPatterns = ChatLogParser.CompileKeywords(ChatLogParser.SplitKeywords(Keywords));
        _dirty = true;
    }

    partial void OnKeywordsChanged(string value)
    {
        _keywordPatterns = ChatLogParser.CompileKeywords(ChatLogParser.SplitKeywords(value));
        MarkDirty();
    }

    partial void OnWatchTellChanged(bool value) => MarkDirty();
    partial void OnWatchLocalChanged(bool value) => MarkDirty();
    partial void OnWatchBroadcastChanged(bool value) => MarkDirty();
    partial void OnWatchRequestChanged(bool value) => MarkDirty();
    partial void OnSendDirectlyChanged(bool value) => MarkDirty();

    partial void OnIsMonitoringChanged(bool value)
    {
        MarkDirty();
        if (_loading)
            return;
        if (value)
            Retarget(clearCards: false);   // start from the end of the current log; what was written while stopped is skipped
        else
            UpdateStatus();
    }

    [RelayCommand]
    private void ToggleMonitoring() => IsMonitoring = !IsMonitoring;

    // ---- watching ----

    private void Retarget(bool clearCards = true)
    {
        if (clearCards)
        {
            Cards.Clear();
            ResultText = "";
        }
        _tailer.SetDirectory(_state.SelectedInstallation is { } inst && _state.SelectedAccount is { } account
            ? Path.Combine(inst.Path, "accounts", account.Name, "logs")
            : null);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = !IsMonitoring ? "Monitoring is stopped."
            : _tailer.Directory is null ? "Select an installation and account."
            : !Directory.Exists(_tailer.Directory) ? $"No logs folder: {_tailer.Directory}"
            : _tailer.CurrentFile is null ? "No chat log yet; waiting for one."
            : $"Watching: {_tailer.CurrentFile}" + (CurrentCharacter is null ? "" : $"   (ignoring {CurrentCharacter})");
    }

    private void Tick()
    {
        try
        {
            if (!IsMonitoring)
            {
                UpdateStatus();
                FlushSettings();
                return;
            }

            var lines = _tailer.Poll();

            // Messages from the character we are logged in as are never shown
            var me = _tailer.CurrentCharacter;
            if (!string.Equals(me, CurrentCharacter, StringComparison.OrdinalIgnoreCase))
            {
                CurrentCharacter = me;
                if (me != null)
                    foreach (var card in Cards.Where(c => c.Key == me.Trim().ToLowerInvariant()).ToList())
                        Cards.Remove(card);
            }

            if (lines.Count > 0)
            {
                var channels = new HashSet<string>();
                if (WatchTell) channels.Add("Tell");
                if (WatchLocal) channels.Add("Local");
                if (WatchBroadcast) channels.Add("Broadcast");
                if (WatchRequest) channels.Add("Request");

                foreach (var line in lines)
                    if (ChatLogParser.Parse(line, channels, _keywordPatterns) is { } request &&
                        !request.Name.Trim().Equals(me, StringComparison.OrdinalIgnoreCase))
                        AddOrUpdate(request);
            }
            UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText = "Log read error: " + ex.Message;
        }
        FlushSettings();
    }

    private void AddOrUpdate(ChatRequest request)
    {
        var key = request.Name.Trim().ToLowerInvariant();
        var existing = Cards.FirstOrDefault(c => c.Key == key);
        if (existing != null)
        {
            if ((DateTime.Now - existing.LastSeen).TotalSeconds <= DuplicateWindowSeconds)
            {
                existing.Update(request);        // same person again: refresh their card
                return;
            }
        }

        var card = new RequestCardViewModel(request, InviteAsync, c => Cards.Remove(c));
        if (existing != null)
            Cards[Cards.IndexOf(existing)] = card;      // an old card of the same person: replace it where it is, nothing else shifts
        else
            Cards.Add(card);                            // new entries go at the end so the rows under the cursor never move
        if (_settings.PlaySound)
            GameSender.Beep();
    }

    // ---- actions ----

    [RelayCommand]
    private void Clear()
    {
        Cards.Clear();
        ResultText = "";
    }

    [RelayCommand]
    private void Refresh()
    {
        _tailer.CheckForNewerFile();
        Tick();
    }

    private async Task InviteAsync(RequestCardViewModel card, string commandPrefix)
    {
        if (_sending)
            return;
        _sending = true;
        try
        {
            var command = $"{commandPrefix} {card.Name}";
            await GameSender.SetClipboardAsync(command);

            if (!SendDirectly)
            {
                ResultText = $"Copied to clipboard: {command}";
                Cards.Remove(card);
                return;
            }

            ResultText = $"Sending: {command}";
            var (ok, message) = await GameSender.SendClipboardAsync(
                new SendDelays(_settings.SwitchDelayMs, _settings.PasteDelayMs, _settings.SendDelayMs));
            ResultText = ok ? $"Sent: {command}" : message;
            if (ok)
                Cards.Remove(card);
        }
        finally
        {
            _sending = false;
        }
    }

    /// <summary>Call before the Options window opens: it edits the file, so write the pane's state first.</summary>
    public void PrepareOptions()
    {
        _dirty = true;
        FlushSettings();
    }

    /// <summary>Called after the Options window saved: picks up the filters and delays that were written.</summary>
    public void ReloadOptions()
    {
        _settings = ChatWatcherSettings.Load();
        var selectedName = SelectedFilter?.Name;
        var keywords = Keywords;
        _loading = true;
        Filters.Clear();
        foreach (var f in _settings.Filters)
            Filters.Add(f);
        SelectedFilter = Filters.FirstOrDefault(f => f.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        _loading = false;
        Keywords = keywords;
    }
}
