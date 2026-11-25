using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.ConfigSync.ViewModels;

public sealed partial class ItemToggle : ObservableObject
{
    public ItemToggle(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Editable copy of one environment's settings.</summary>
public sealed partial class EnvironmentEditor : ObservableObject
{
    // The environment's own values while it is showing the default's values (use default checked)
    private EnvironmentSettings? _own;

    public EnvironmentEditor(EnvironmentSettings settings, bool isDefault)
    {
        Name = settings.Name;
        _gameEnvironment = SyncItems.GameEnvironments.Contains(settings.GameEnvironment)
            ? settings.GameEnvironment : SyncItems.DefaultGameEnvironment;
        ItemToggles = SyncItems.All.Select(i => new ItemToggle(i.Key, i.DisplayName)).ToList();
        _isDefault = isDefault;
        _useDefault = settings.UseDefault && !isDefault;
        Apply(settings);
        if (_useDefault)
            _own = Snapshot();
    }

    public string Name { get; }
    public IReadOnlyList<ItemToggle> ItemToggles { get; }
    public IReadOnlyList<string> GameEnvironments => SyncItems.GameEnvironments;

    /// <summary>Folder under settings this installation uses; belongs to the installation, so it is not inherited.</summary>
    [ObservableProperty]
    private string _gameEnvironment = SyncItems.DefaultGameEnvironment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeDefault), nameof(IsEditable))]
    private bool _isDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _useDefault;

    [ObservableProperty]
    private string _backupFolder = "";

    [ObservableProperty]
    private bool _mergeLogfiles;

    [ObservableProperty]
    private bool _mergePrivateLogs;

    /// <summary>The default environment can only be changed by making another one the default.</summary>
    public bool CanChangeDefault => !IsDefault;

    /// <summary>Options are greyed out while inheriting the default's settings.</summary>
    public bool IsEditable => IsDefault || !UseDefault;

    public EnvironmentSettings Snapshot()
    {
        var s = new EnvironmentSettings(Name)
        {
            UseDefault = UseDefault,
            BackupFolder = BackupFolder,
            GameEnvironment = GameEnvironment,
            MergeLogfiles = MergeLogfiles,
            MergePrivateLogs = MergePrivateLogs,
        };
        foreach (var t in ItemToggles.Where(t => t.IsChecked))
            s.EnabledItems.Add(t.Key);
        return s;
    }

    private void Apply(EnvironmentSettings s)
    {
        BackupFolder = s.BackupFolder;
        MergeLogfiles = s.MergeLogfiles;
        MergePrivateLogs = s.MergePrivateLogs;
        foreach (var t in ItemToggles)
            t.IsChecked = s.EnabledItems.Contains(t.Key);
    }

    /// <summary>Shows the default's values (read-only) while "use default" is on; remembers our own to restore.</summary>
    public void ShowInherited(EnvironmentEditor defaultEditor)
    {
        _own ??= Snapshot();
        Apply(defaultEditor.Snapshot());
    }

    public void RestoreOwn()
    {
        if (_own != null)
            Apply(_own);
        _own = null;
    }

    /// <summary>What gets written: while inheriting, our own (hidden) values plus the use-default flag.</summary>
    public EnvironmentSettings ToSettings()
    {
        var s = UseDefault && _own != null ? _own.Clone() : Snapshot();
        s.UseDefault = UseDefault && !IsDefault;
        s.GameEnvironment = GameEnvironment;
        return s;
    }
}

public sealed partial class ConfigSyncOptionsViewModel : OptionsSectionViewModel
{
    private readonly ConfigSyncSettings _settings;
    private bool _updating;

    public ConfigSyncOptionsViewModel(IReadOnlyList<string> environmentNames, ConfigSyncSettings settings, AppState? state = null)
    {
        _settings = settings;
        _validateOrphanedLogs = settings.ValidateOrphanedLogs;
        foreach (var name in environmentNames)
        {
            var env = settings.Find(name) ?? ConfigSyncSettings.NewEnvironment(name, false);
            var editor = new EnvironmentEditor(env, name.Equals(settings.DefaultEnvironment, StringComparison.OrdinalIgnoreCase));
            editor.PropertyChanged += (_, e) => OnEditorChanged(editor, e.PropertyName);
            Environments.Add(editor);
        }

        foreach (var e in Environments.Where(e => e.UseDefault && DefaultEditor != null))
            e.ShowInherited(DefaultEditor!);
        // There is no picker: the settings shown are always those of the installation selected in the status bar
        SelectedEnvironment = EditorFor(state?.SelectedInstallation?.Name)
                              ?? Environments.FirstOrDefault(e => e.IsDefault) ?? Environments.FirstOrDefault();
        if (state != null)
        {
            // Follows the status bar while this page is open (a weak link, so closed pages are not kept alive)
            var weak = new WeakReference<ConfigSyncOptionsViewModel>(this);
            System.ComponentModel.PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (!weak.TryGetTarget(out var vm))
                    state.PropertyChanged -= handler;
                else if (e.PropertyName == nameof(AppState.SelectedInstallation) && vm.EditorFor(state.SelectedInstallation?.Name) is { } editor)
                    vm.SelectedEnvironment = editor;
            };
            state.PropertyChanged += handler;
        }
    }

    public override string Title => "Config Sync";

    private EnvironmentEditor? EditorFor(string? installationName) =>
        installationName is null ? null : Environments.FirstOrDefault(e => e.Name.Equals(installationName, StringComparison.OrdinalIgnoreCase));

    public ObservableCollection<EnvironmentEditor> Environments { get; } = new();

    [ObservableProperty]
    private EnvironmentEditor? _selectedEnvironment;

    /// <summary>Applies to all environments.</summary>
    [ObservableProperty]
    private bool _validateOrphanedLogs;

    partial void OnSelectedEnvironmentChanged(EnvironmentEditor? value)
    {
        // The default's values may have been edited since this environment last showed them
        if (value is { UseDefault: true, IsDefault: false } && DefaultEditor is { } def)
            value.ShowInherited(def);
    }

    private EnvironmentEditor? DefaultEditor => Environments.FirstOrDefault(e => e.IsDefault);

    private void OnEditorChanged(EnvironmentEditor editor, string? property)
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            if (property == nameof(EnvironmentEditor.IsDefault) && editor.IsDefault)
            {
                // Only one default; the new default has its own settings
                foreach (var other in Environments.Where(o => o != editor))
                    other.IsDefault = false;
                if (editor.UseDefault)
                {
                    editor.UseDefault = false;
                    editor.RestoreOwn();
                }
                foreach (var inheritor in Environments.Where(o => o.UseDefault && !o.IsDefault))
                    inheritor.ShowInherited(editor);
            }
            else if (property == nameof(EnvironmentEditor.UseDefault) && !editor.IsDefault)
            {
                if (editor.UseDefault && DefaultEditor is { } def)
                    editor.ShowInherited(def);
                else
                    editor.RestoreOwn();
            }
        }
        finally
        {
            _updating = false;
        }
    }

    public override string? Save()
    {
        // Nothing is written until here
        _settings.Environments.Clear();
        foreach (var e in Environments)
            _settings.Environments.Add(e.ToSettings());
        _settings.DefaultEnvironment = DefaultEditor?.Name ?? _settings.DefaultEnvironment;
        _settings.ValidateOrphanedLogs = ValidateOrphanedLogs;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return "Could not write CityHelper.ini:\n" + ex.Message;
        }
        return null;
    }
}
