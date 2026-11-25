using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CityHelper.Ribbon;
using CityHelper.ViewModels;
using CityHelper.Tools.PopMenuHero.Services;
using CityHelper.Tools.PopMenuHero.Core;

namespace CityHelper.Tools.PopMenuHero.ViewModels;

public enum DropPosition
{
    Before,
    After,
    Inside,
}

public partial class PopMenuHeroViewModel : ViewModelBase, IEditHost
{
    private const int MaxUndo = 10;

    // Every expanded node gets its own row control; past this, fully expanding takes seconds
    // (a 59k-node file takes ~30s), so large files open with only the root menu expanded.
    private const int ExpandAllLimit = 1500;

    private readonly IDialogService _dialogs;
    private readonly CityHelper.Services.AppState _shell;
    private readonly List<string> _undoStack = [];   // serialized states, newest last
    private bool _fieldSessionActive;                 // true while editing fields on the current item
    private MnuNode? _clipboard;

    public PopMenuHeroViewModel(IDialogService dialogs, CityHelper.Services.AppState shell)
    {
        _dialogs = dialogs;
        _shell = shell;
        RootItems.CollectionChanged += OnRootItemsChanged;
    }

    public IDialogService Dialogs => _dialogs;

    /// <summary>Top level of the tree: root comments, an optional root Title, and root Menus.</summary>
    public ObservableCollection<NodeViewModel> RootItems { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(DeleteSelectedCommand))]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _currentFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isModified;


    private IReadOnlyList<RibbonCommand>? _toolbarCommands;

    /// <summary>Icon buttons for the toolbar in the view's title bar ("-" entries are dividers).</summary>
    public IReadOnlyList<RibbonCommand> ToolbarCommands => _toolbarCommands ??= new[]
    {
        new RibbonCommand("New", NewCommand, "New (Ctrl+N)"),
        new RibbonCommand("Open", OpenCommand, "Open (Ctrl+O)"),
        new RibbonCommand("Save", SaveCommand, "Save (Ctrl+S)"),
        new RibbonCommand("Save As", SaveAsCommand, "Save As (Ctrl+Shift+S)"),
        new RibbonCommand("Import", ImportMenuCommand, "Import (Ctrl+I)"),
        RibbonCommand.Separator,
        new RibbonCommand("Undo", UndoCommand, "Undo (Ctrl+Z)"),
        new RibbonCommand("Copy", CopyCommand, "Copy (Ctrl+C)"),
        new RibbonCommand("Paste", PasteCommand, "Paste (Ctrl+V)"),
        new RibbonCommand("Delete", DeleteSelectedCommand, "Delete selected (Del)"),
        RibbonCommand.Separator,
        new RibbonCommand("Expand All", ExpandAllCommand, "Expand all"),
        new RibbonCommand("Collapse Root", CollapseToRootCommand, "Collapse to root"),
        new RibbonCommand("Collapse All", CollapseAllCommand, "Collapse all"),
        new RibbonCommand("MNU Preview", OpenPreviewCommand, "MNU preview"),
    };

    public string WindowTitle =>
        $"PopMenu Hero — {(CurrentFile is null ? "Untitled" : Path.GetFileName(CurrentFile))}{(IsModified ? " *" : "")}";

    public bool CanUndo => _undoStack.Count > 0;

    public string UndoToolTip => _undoStack.Count switch
    {
        0 => "Nothing to undo",
        1 => "Undo (1 step available)",
        var n => $"Undo ({n} steps available)",
    };

    private bool HasSelection => SelectedNode != null;
    private bool CanPaste => _clipboard != null;

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        _fieldSessionActive = false;
        UpdatePreview(rebuild: false);
    }

    // --- Startup ---

    private bool _initialized;

    public async Task InitializeAsync(string[] args)
    {
        if (_initialized)
            return;
        _initialized = true;
        if (args.Length > 0 && args[0].EndsWith(".mnu", StringComparison.OrdinalIgnoreCase) && File.Exists(args[0]))
            await OpenPathAsync(args[0]);
        else
            NewDocument();
    }

    // --- Document <-> tree ---

    private void LoadDocument(MnuFile mnu)
    {
        SelectedNode = null;
        RootItems.Clear();
        foreach (var comment in mnu.RootComments)
            RootItems.Add(NodeViewModel.Create(comment, this));
        foreach (var menu in mnu.RootMenus)
        {
            // If the first child of a root Menu is a Title, hoist it to root level for display
            if (menu.Children.Count > 0 && menu.Children[0] is TitleNode title)
            {
                RootItems.Add(NodeViewModel.Create(title, this));
                menu.Children.RemoveAt(0);
            }
            RootItems.Add(NodeViewModel.Create(menu, this));
        }
        if (mnu.RootMenus.Count > 0)
        {
            if (AllNodes().Count() <= ExpandAllLimit)
                ExpandAllNodes();
            else
                CollapseToRoot();
        }
        UpdatePreview(rebuild: true);
    }

    public MnuFile ToMnuFile()
    {
        var mnu = new MnuFile();
        TitleNode? pendingTitle = null;
        foreach (var item in RootItems)
        {
            switch (item)
            {
                case CommentNodeViewModel c:
                    mnu.RootComments.Add((CommentNode)c.Model);
                    break;
                case TitleNodeViewModel t:
                    // Root-level Title gets absorbed as first child of the following Menu on save
                    pendingTitle = (TitleNode)t.Model;
                    break;
                case MenuNodeViewModel m:
                    var menu = (MenuNode)m.ToModel();
                    if (pendingTitle != null)
                    {
                        menu.Children.Insert(0, pendingTitle);
                        pendingTitle = null;
                    }
                    mnu.RootMenus.Add(menu);
                    break;
            }
        }
        return mnu;
    }

    private string Snapshot() => new MnuWriter().Write(ToMnuFile());

    private void OnRootItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (NodeViewModel n in e.NewItems)
                n.Parent = null;
    }

    private IList<NodeViewModel> SiblingsOf(NodeViewModel node) => node.Parent?.Children ?? (IList<NodeViewModel>)RootItems;

    private IEnumerable<NodeViewModel> AllNodes() => RootItems.SelectMany(n => n.SelfAndDescendants());

    private int FindRootMenuIndex()
    {
        for (int i = 0; i < RootItems.Count; i++)
            if (RootItems[i] is MenuNodeViewModel)
                return i;
        return -1;
    }

    // --- Edit tracking (IEditHost) ---

    void IEditHost.BeforeEdit(NodeViewModel node)
    {
        // First field change per selection session records one undo step
        if (!_fieldSessionActive)
        {
            PushUndo(Snapshot());
            _fieldSessionActive = true;
        }
    }

    void IEditHost.AfterEdit(NodeViewModel node) => MarkModified();

    private void MarkModified()
    {
        IsModified = true;
        UpdatePreview(rebuild: true);
    }

    // --- MNU preview window ---

    private readonly MnuPreviewViewModel _preview = new();
    private Dictionary<MnuNode, (int Start, int End)>? _previewRanges;

    [RelayCommand]
    private void OpenPreview()
    {
        _preview.IsOpen = true;
        _dialogs.ShowPreview(_preview);
        UpdatePreview(rebuild: true);
    }

    /// <summary>
    /// Refreshes the preview window if it is open. Selection changes only move the highlight;
    /// document changes (<paramref name="rebuild"/>) re-serialize the whole file.
    /// </summary>
    private void UpdatePreview(bool rebuild)
    {
        if (!_preview.IsOpen)
            return;

        if (rebuild || _previewRanges is null)
        {
            _previewRanges = new Dictionary<MnuNode, (int Start, int End)>(ReferenceEqualityComparer.Instance);
            _preview.SetText(new MnuWriter().Write(ToMnuFile(), _previewRanges));
        }

        (int Start, int End)? range = null;
        if (SelectedNode != null && _previewRanges.TryGetValue(SelectedNode.Model, out var r))
            range = r;
        _preview.Highlight(range);
    }

    // --- Undo ---

    private void PushUndo(string snapshot)
    {
        _undoStack.Add(snapshot);
        if (_undoStack.Count > MaxUndo)
            _undoStack.RemoveAt(0);
        OnUndoChanged();
    }

    private void ResetUndo()
    {
        _undoStack.Clear();
        _fieldSessionActive = false;
        OnUndoChanged();
    }

    private void OnUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoToolTip));
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;
        var text = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        LoadDocument(new MnuParser().Parse(text));
        _fieldSessionActive = false;
        MarkModified();
        OnUndoChanged();
    }

    // --- Insert ---

    private async Task InsertNewAsync(MnuNode node)
    {
        var snapshot = Snapshot();
        if (!await TryInsertAsync(NodeViewModel.Create(node, this)))
            return;
        PushUndo(snapshot);
        MarkModified();
        // Edits to the freshly added item belong to the same undo step
        _fieldSessionActive = true;
    }

    /// <summary>
    /// Places a node relative to the selection, following the .mnu structure rules.
    /// Returns false (after telling the user why) if it can't go there.
    /// </summary>
    private async Task<bool> TryInsertAsync(NodeViewModel node)
    {
        var sel = SelectedNode;

        // Root-level Comment: only go to root if selection is root-level or empty
        if (node is CommentNodeViewModel)
        {
            if (sel is { Parent: null })
            {
                InsertAndSelect(RootItems, RootItems.IndexOf(sel) + 1, node);
                return true;
            }
            if (sel is null)
            {
                // Nothing selected — insert before the root Menu at root level
                int menuIdx = FindRootMenuIndex();
                InsertAndSelect(RootItems, menuIdx >= 0 ? menuIdx : RootItems.Count, node);
                return true;
            }
            // Otherwise fall through to normal nested insertion below
        }

        // Root-level Title: immediately before the root Menu
        if (node is TitleNodeViewModel && sel is { Parent: null })
        {
            int menuIdx = FindRootMenuIndex();
            if (menuIdx < 0)
            {
                await _dialogs.ShowMessageAsync("No Root Menu",
                    "Add a root Menu before adding a root-level Title.", MessageKind.Warning);
                return false;
            }
            if (RootItems.Any(n => n is TitleNodeViewModel))
            {
                await _dialogs.ShowMessageAsync("Title Already Exists",
                    "There is already a root-level Title immediately before the Menu.\n" +
                    "Edit it in the properties panel, or delete it first.", MessageKind.Warning);
                return false;
            }
            InsertAndSelect(RootItems, menuIdx, node);
            return true;
        }

        // All other nodes go inside a Menu
        var (found, menu, index) = GetInsertTarget();
        if (!found)
        {
            if (node is MenuNodeViewModel)
            {
                InsertAndSelect(RootItems, RootItems.Count, node);
                return true;
            }
            await _dialogs.ShowMessageAsync("No Root Menu",
                "Please create a Menu element first — it must be the root.", MessageKind.Warning);
            return false;
        }
        if (menu is null)
        {
            await _dialogs.ShowMessageAsync("Invalid Parent",
                "Only Menu elements can contain children.", MessageKind.Warning);
            return false;
        }

        menu.IsExpanded = true;
        InsertAndSelect(menu.Children, index, node);
        return true;
    }

    /// <summary>Where a new node goes: Menu is null when the target position is the root level.</summary>
    private (bool Found, MenuNodeViewModel? Menu, int Index) GetInsertTarget()
    {
        var sel = SelectedNode;
        if (sel is null)
        {
            // Nothing selected — append to end of root Menu
            var root = RootItems.OfType<MenuNodeViewModel>().FirstOrDefault();
            return root is null ? (false, null, 0) : (true, root, root.Children.Count);
        }

        // If a Menu is selected and expanded, insert as its first child
        if (sel is MenuNodeViewModel menu && menu.IsExpanded)
            return (true, menu, 0);

        // Otherwise insert after the selected item in its parent
        return (true, sel.Parent, SiblingsOf(sel).IndexOf(sel) + 1);
    }

    private void InsertAndSelect(IList<NodeViewModel> list, int index, NodeViewModel node)
    {
        list.Insert(index, node);
        SelectedNode = node;
    }

    [RelayCommand]
    private Task AddMenu() => InsertNewAsync(new MenuNode("New Menu"));

    [RelayCommand]
    private Task AddOption() => InsertNewAsync(new OptionNode("New Option", ""));

    [RelayCommand]
    private Task AddLockedOption() => InsertNewAsync(new LockedOptionNode { DisplayName = "New LockedOption" });

    [RelayCommand]
    private Task AddTitle() => InsertNewAsync(new TitleNode("New Title"));

    [RelayCommand]
    private Task AddText() => InsertNewAsync(new TextNode("New Text"));

    [RelayCommand]
    private Task AddDivider() => InsertNewAsync(new DividerNode());

    [RelayCommand]
    private Task AddComment() => InsertNewAsync(new CommentNode("New comment"));

    [RelayCommand]
    private Task AddDialog() => InsertNewAsync(new DialogNode("New Dialog", ""));

    // --- Delete / Copy / Paste ---

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelected()
    {
        var node = SelectedNode;
        if (node is null)
            return;
        // Only confirm when deleting a root Menu (would remove all children too)
        if (node is MenuNodeViewModel && node.Parent is null &&
            !await _dialogs.ConfirmAsync("Delete Root Menu", "Delete the root menu and all its children?"))
            return;

        var snapshot = Snapshot();
        SiblingsOf(node).Remove(node);
        SelectedNode = null;
        PushUndo(snapshot);
        MarkModified();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (SelectedNode is null)
            return;
        _clipboard = SelectedNode.ToModel().Clone();
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task Paste()
    {
        if (_clipboard is null)
            return;
        var node = _clipboard.Clone();
        var sel = SelectedNode;
        var snapshot = Snapshot();
        bool inserted;

        if (node is MenuNode menu && sel is { Parent: null })
        {
            // Pasting next to a root-level item → new root menu
            menu.Name = MakeUniqueRootName(menu.Name);
            InsertAndSelect(RootItems, RootItems.IndexOf(sel) + 1, NodeViewModel.Create(menu, this));
            inserted = true;
        }
        else
        {
            inserted = await TryInsertAsync(NodeViewModel.Create(node, this));
        }

        if (inserted)
        {
            PushUndo(snapshot);
            MarkModified();
        }
        _fieldSessionActive = false;
    }

    private string MakeUniqueRootName(string name)
    {
        var existing = RootItems.OfType<MenuNodeViewModel>().Select(m => m.Name).ToHashSet();
        if (!existing.Contains(name))
            return name;
        var stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        for (int counter = 1; ; counter++)
        {
            var candidate = $"{stem}{counter}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    // --- Drag and drop ---

    /// <summary>
    /// Whether <paramref name="source"/> may be dropped relative to <paramref name="target"/>
    /// (null target = empty space below the tree, i.e. end of the root level).
    /// Only Menus contain children; the root level only holds what a .mnu file can store there.
    /// </summary>
    public bool CanDrop(NodeViewModel source, NodeViewModel? target, DropPosition position)
    {
        if (target is null)
            return source is MenuNodeViewModel or CommentNodeViewModel;
        if (target == source || target.IsDescendantOf(source))
            return false;
        if (position == DropPosition.Inside)
            return target is MenuNodeViewModel;
        if (target.Parent != null)
            return true;

        // Dropping beside a root-level item
        if (source is TitleNodeViewModel)
        {
            // A root Title only survives saving when it sits directly before a root Menu
            if (position != DropPosition.Before || target is not MenuNodeViewModel)
                return false;
            int idx = RootItems.IndexOf(target);
            return idx == 0 || RootItems[idx - 1] is not TitleNodeViewModel t || t == source;
        }
        return source is MenuNodeViewModel or CommentNodeViewModel;
    }

    public void MoveNode(NodeViewModel source, NodeViewModel? target, DropPosition position)
    {
        if (!CanDrop(source, target, position))
            return;

        var snapshot = Snapshot();
        SiblingsOf(source).Remove(source);
        if (target is null)
        {
            RootItems.Add(source);
        }
        else if (position == DropPosition.Inside)
        {
            var menu = (MenuNodeViewModel)target;
            menu.Children.Add(source);
            menu.IsExpanded = true;
        }
        else
        {
            var list = SiblingsOf(target);
            int idx = list.IndexOf(target);
            list.Insert(position == DropPosition.After ? idx + 1 : idx, source);
        }
        PushUndo(snapshot);
        MarkModified();
        SelectedNode = source;
    }

    // --- Expand / collapse ---

    [RelayCommand]
    private async Task ExpandAll()
    {
        int count = AllNodes().Count();
        if (count > ExpandAllLimit &&
            !await _dialogs.ConfirmAsync("Expand All",
                $"This menu has {count:N0} items. Expanding all of them may take a while.\n\nContinue?"))
            return;
        ExpandAllNodes();
    }

    private void ExpandAllNodes()
    {
        foreach (var n in AllNodes())
            n.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var n in AllNodes())
            n.IsExpanded = false;
    }

    /// <summary>Collapse all items but keep the root menu expanded.</summary>
    [RelayCommand]
    private void CollapseToRoot()
    {
        CollapseAll();
        var root = RootItems.OfType<MenuNodeViewModel>().FirstOrDefault();
        if (root != null)
            root.IsExpanded = true;
    }

    // --- File operations ---

    private void NewDocument()
    {
        ResetUndo();
        CurrentFile = null;
        LoadDocument(new MnuFile { RootMenus = [new MenuNode("NewMenu")] });
        // Leave modified = false so opening a file right away doesn't prompt
        IsModified = false;
    }

    [RelayCommand]
    private async Task New()
    {
        if (await ConfirmDiscardAsync())
            NewDocument();
    }

    [RelayCommand]
    private async Task Open()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var path = await _dialogs.PickOpenFileAsync("Open PopMenu File", GetDefaultDirectory());
        if (path != null)
            await OpenPathAsync(path);
    }

    public async Task OpenPathAsync(string path)
    {
        try
        {
            LoadDocument(MnuFileIO.Load(path));
            CurrentFile = path;
            IsModified = false;
            ResetUndo();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Error Opening File", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        // Save always targets the selected installation's popmenu folder; Save As is the way out of it.
        var dir = EnsureMenusDirectory();
        if (dir is null)
        {
            await SaveAs();
            return;
        }

        string name;
        if (CurrentFile != null)
        {
            name = Path.GetFileName(CurrentFile);
        }
        else
        {
            var root = RootItems.OfType<MenuNodeViewModel>().FirstOrDefault();
            name = SafeFileName(root?.Name) + ".mnu";
        }

        var target = Path.Combine(dir, name);
        bool sameFile = CurrentFile != null &&
            string.Equals(Path.GetFullPath(CurrentFile), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(target) &&
            !await _dialogs.ConfirmAsync("Overwrite File", $"{target}\n\nalready exists. Overwrite it?"))
            return;

        await SaveToAsync(target);
    }

    private static string SafeFileName(string? name)
    {
        var cleaned = string.Concat((name ?? "").Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0)).Trim();
        return cleaned.Length > 0 ? cleaned : "Untitled";
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        // Suggest filename based on root menu name
        var root = RootItems.OfType<MenuNodeViewModel>().FirstOrDefault();
        var path = await _dialogs.PickSaveFileAsync("Save PopMenu File", GetDefaultDirectory(),
            root != null ? root.Name + ".mnu" : null);
        if (path != null)
            await SaveToAsync(path);
    }

    public async Task SaveToAsync(string path)
    {
        try
        {
            MnuFileIO.Save(ToMnuFile(), path);
            CurrentFile = path;
            IsModified = false;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Error Saving File", ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Open another .mnu file and insert its root menu(s) as submenus here.</summary>
    [RelayCommand]
    private async Task ImportMenu()
    {
        var path = await _dialogs.PickOpenFileAsync("Import Menu from File", GetDefaultDirectory());
        if (path is null)
            return;
        MnuFile mnu;
        try
        {
            mnu = MnuFileIO.Load(path);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Import Error", $"Failed to read file:\n{ex.Message}", MessageKind.Error);
            return;
        }
        if (mnu.RootMenus.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Import Error", "The selected file has no root menus to import.", MessageKind.Warning);
            return;
        }

        var snapshot = Snapshot();
        bool any = false;
        foreach (var menu in mnu.RootMenus)
            any |= await TryInsertAsync(NodeViewModel.Create(menu, this));
        if (any)
        {
            PushUndo(snapshot);
            MarkModified();
        }
        _fieldSessionActive = false;
    }

    public async Task<bool> ConfirmDiscardAsync() =>
        !IsModified || await _dialogs.ConfirmAsync("Unsaved Changes", "There are unsaved changes. Discard them?");

    [RelayCommand]
    private void Exit() => _dialogs.CloseWindow();

    // --- Installation popmenu folder ---

    /// <summary>The popmenu folder of the currently selected installation (may not exist), or null if none is selected.</summary>
    private string? MenusDirectory =>
        _shell.SelectedInstallation is { } i
            ? Path.Combine(i.Path, "data", "texts", "English", "menus")
            : null;

    /// <summary>Start location for pickers: the selected installation's popmenu folder when it exists.</summary>
    private string? GetDefaultDirectory() => EnsureMenusDirectory();

    /// <summary>
    /// Returns the selected installation's popmenu folder, creating it if the installation folder exists
    /// but the menus folder doesn't. Null if no installation is selected or its folder is missing.
    /// </summary>
    private string? EnsureMenusDirectory()
    {
        if (MenusDirectory is not { } menus || _shell.SelectedInstallation is not { } inst || !Directory.Exists(inst.Path))
            return null;
        try
        {
            Directory.CreateDirectory(menus);
            return menus;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // --- Help ---

    [RelayCommand]
    private void OpenUrl(string url) => _dialogs.OpenUrl(url);

    [RelayCommand]
    private Task About() => _dialogs.ShowMessageAsync("About PopMenu Hero",
        "PopMenu Hero\n\n" +
        "Created 2026 by RayolaRayons\n\n" +
        "For the latest version go to the forum or the GitHub repository.");
}
