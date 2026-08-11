using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features;

public partial class UnlockedShellView : UserControl
{
    private const double CompactContentBreakpoint = 920;
    private const double CompactPaneWidth = 48;
    private const double ExpandedPaneWidth = 248;

    private static readonly DeferredNavigationItem[] DeferredVaultItems =
    [
        new("L.SecureNotes", "Notes", FASymbol.ProtectedDocument),
        new("L.Totp", "Totp", FASymbol.Clock),
        new("L.Cards", "Cards", FASymbol.ContactInfo)
    ];

    private static readonly DeferredNavigationItem[] DeferredToolItems =
    [
        new("L.Generator", "Generator", FASymbol.Edit),
        new("L.SecurityAnalysis", "SecurityAnalysis", FASymbol.Permissions),
        new("L.Timeline", "Timeline", FASymbol.Clock)
    ];

    private static readonly DeferredNavigationItem[] DeferredStorageItems =
    [
        new("L.Archive", "Archive", FASymbol.Library),
        new("L.RecycleBin", "RecycleBin", FASymbol.Delete),
        new("L.MdbxVaults", "Mdbx", FASymbol.Folder)
    ];

    private static readonly DeferredNavigationItem[] DeferredFooterItems =
    [
        new("L.DatabaseManagement", "DatabaseManagement", FASymbol.Library),
        new("L.SyncAndBackup", "Sync", FASymbol.Sync),
        new("L.Settings", "Settings", FASymbol.Setting)
    ];

    private readonly WorkspaceHostView _workspaceHost;
    private Grid? _workspaceScaffold;
    private bool _workspaceScaffoldInitialized;
    private bool _shellChromeInitialized;
    private bool _deferredNavigationInitialized;

    public UnlockedShellView()
    {
        _workspaceHost = new WorkspaceHostView();
        _workspaceHost.Bind(
            WorkspaceHostView.SectionProperty,
            new Binding(nameof(MainWindowViewModel.SelectedSection)));
        _workspaceHost.SizeChanged += WorkspaceHost_OnSizeChanged;
        SizeChanged += Shell_OnSizeChanged;
        Content = CreateLoadingPlaceholder();
        Dispatcher.UIThread.Post(InitializeWorkspaceScaffold, DispatcherPriority.Background);
    }

    private void InitializeWorkspaceScaffold()
    {
        if (_workspaceScaffoldInitialized)
        {
            return;
        }

        _workspaceScaffoldInitialized = true;
        _workspaceScaffold = new Grid { Margin = new Thickness(18) };
        _workspaceScaffold.Children.Add(_workspaceHost);
        Content = _workspaceScaffold;
        Dispatcher.UIThread.Post(InitializeDeferredShellChrome, DispatcherPriority.SystemIdle);
    }

    private void InitializeDeferredShellChrome()
    {
        if (_shellChromeInitialized || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _shellChromeInitialized = true;
        _workspaceScaffold?.Children.Remove(_workspaceHost);
        _workspaceScaffold = null;
        InitializeComponent();
        UpdateShellLayout(Bounds.Width);
        UpdateCompactNavigationChrome();
        WorkspaceHostSlot.Content = _workspaceHost;
        Dispatcher.UIThread.Post(InitializeDeferredNavigation, DispatcherPriority.SystemIdle);
    }

    private void InitializeDeferredNavigation()
    {
        if (_deferredNavigationInitialized || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        _deferredNavigationInitialized = true;
        AddNavigationItems(DeferredVaultItems);
        VaultNavigationView.MenuItems.Add(CreateNavigationHeader("L.ToolsNavigationGroup", "Tools"));
        AddNavigationItems(DeferredToolItems);
        VaultNavigationView.MenuItems.Add(CreateNavigationHeader("L.StorageNavigationGroup", "Storage"));
        AddNavigationItems(DeferredStorageItems);

        for (var index = 0; index < DeferredFooterItems.Length; index++)
        {
            VaultNavigationView.FooterMenuItems.Insert(
                index,
                CreateNavigationItem(DeferredFooterItems[index]));
        }

        var footerSeparator = new FANavigationViewItemSeparator();
        footerSeparator.Classes.Add("shellNavSeparator");
        VaultNavigationView.FooterMenuItems.Add(footerSeparator);
        var lockItem = CreateNavigationItem(new DeferredNavigationItem("LockVaultText", "Lock", FASymbol.Admin));
        lockItem.Name = "LockVaultNavigationItem";
        lockItem.SelectsOnInvoked = false;
        VaultNavigationView.FooterMenuItems.Add(lockItem);
        UpdateCompactNavigationChrome();
    }

    private void AddNavigationItems(IEnumerable<DeferredNavigationItem> items)
    {
        foreach (var item in items)
        {
            VaultNavigationView.MenuItems.Add(CreateNavigationItem(item));
        }
    }

    private static FANavigationViewItemHeader CreateNavigationHeader(string labelPath, string tag)
    {
        var header = new FANavigationViewItemHeader { Tag = tag };
        header.Classes.Add("shellNavHeader");
        header.Bind(ContentControl.ContentProperty, new Binding(labelPath));
        return header;
    }

    private static FANavigationViewItem CreateNavigationItem(DeferredNavigationItem source)
    {
        var item = new FANavigationViewItem
        {
            Tag = source.Tag,
            IconSource = new FASymbolIconSource { Symbol = source.Symbol }
        };
        item.Classes.Add("shellNavItem");
        item.Bind(ContentControl.ContentProperty, new Binding(source.LabelPath));
        // Compact rail only shows icons; keep the full label available on hover.
        item.Bind(ToolTip.TipProperty, new Binding(source.LabelPath));
        return item;
    }

    private static Control CreateLoadingPlaceholder() =>
        new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Monica",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontSize = 20
                        },
                        new ProgressBar
                        {
                            Width = 220,
                            Height = 4,
                            IsIndeterminate = true
                        }
                    }
                }
            }
        };

    private void NavigationView_OnSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        var tag = (e.SelectedItem as Control)?.Tag?.ToString()
            ?? (e.SelectedItemContainer as Control)?.Tag?.ToString();
        ActivateNavigationTag(tag);
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        var tag = (e.InvokedItemContainer as Control)?.Tag?.ToString();
        if (!string.Equals(tag, "Lock", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ActivateNavigationTag(tag);
    }

    private void NavigationView_OnPaneOpening(FANavigationView sender, EventArgs e) =>
        UpdateCompactNavigationChrome();

    private void NavigationView_OnPaneOpened(FANavigationView sender, EventArgs e) =>
        UpdateCompactNavigationChrome();

    private void NavigationView_OnPaneClosing(FANavigationView sender, FANavigationViewPaneClosingEventArgs e) =>
        UpdateCompactNavigationChrome();

    private void NavigationView_OnPaneClosed(FANavigationView sender, EventArgs e) =>
        UpdateCompactNavigationChrome();

    private void NavigationView_OnDisplayModeChanged(object? sender, FANavigationViewDisplayModeChangedEventArgs e) =>
        UpdateCompactNavigationChrome();

    internal void ActivateNavigationTag(string? tag)
    {
        if (DataContext is not MainWindowViewModel viewModel || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (string.Equals(tag, "Lock", StringComparison.OrdinalIgnoreCase))
        {
            if (viewModel.LockCommand.CanExecute(null))
            {
                viewModel.LockCommand.Execute(null);
            }

            return;
        }

        viewModel.SelectSectionCommand.Execute(tag);
    }

    private void Shell_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_shellChromeInitialized)
        {
            UpdateShellLayout(e.NewSize.Width);
            UpdateCompactNavigationChrome();
        }
    }

    private void UpdateShellLayout(double width)
    {
        if (WorkspaceContentGrid is null)
        {
            return;
        }

        WorkspaceContentGrid.Margin = width > 0 && width < CompactContentBreakpoint
            ? new Thickness(12, 10, 12, 10)
            : new Thickness(16, 12, 16, 12);
    }

    private void UpdateCompactNavigationChrome()
    {
        if (!_shellChromeInitialized || VaultNavigationView is null || ShellStatusBarContent is null)
        {
            return;
        }

        var compactRail = IsCompactRailVisible();
        SetCompactNavigationClasses(VaultNavigationView.MenuItems, compactRail);
        SetCompactNavigationClasses(VaultNavigationView.FooterMenuItems, compactRail);

        var paneWidth = ResolveVisiblePaneWidth();
        // Keep status text aligned with workspace content instead of under the icon rail.
        ShellStatusBarContent.Padding = new Thickness(paneWidth + 12, 0, 12, 0);
    }

    private bool IsCompactRailVisible()
    {
        // LeftCompact keeps the compact rail as its display mode even while the
        // pane is open. The actual pane state is therefore the source of truth.
        return !VaultNavigationView.IsPaneOpen
            || VaultNavigationView.DisplayMode == FANavigationViewDisplayMode.Minimal;
    }

    private double ResolveVisiblePaneWidth()
    {
        if (VaultNavigationView.PaneDisplayMode is FANavigationViewPaneDisplayMode.Top
            or FANavigationViewPaneDisplayMode.LeftMinimal)
        {
            return VaultNavigationView.IsPaneOpen ? 0 : 0;
        }

        if (VaultNavigationView.DisplayMode == FANavigationViewDisplayMode.Minimal)
        {
            return 0;
        }

        if (VaultNavigationView.IsPaneOpen)
        {
            return VaultNavigationView.OpenPaneLength > 0
                ? VaultNavigationView.OpenPaneLength
                : ExpandedPaneWidth;
        }

        return VaultNavigationView.CompactPaneLength > 0
            ? VaultNavigationView.CompactPaneLength
            : CompactPaneWidth;
    }

    private static void SetCompactNavigationClasses(System.Collections.IEnumerable items, bool compactRail)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case FANavigationViewItemHeader header:
                    header.Classes.Set("compactHidden", compactRail);
                    break;
                case FANavigationViewItem navigationItem:
                    navigationItem.Classes.Set("compact", compactRail);
                    break;
                case FANavigationViewItemSeparator separator:
                    separator.Classes.Set("compactHidden", compactRail);
                    separator.Classes.Set("shellNavSeparator", true);
                    break;
            }
        }
    }

    private void WorkspaceHost_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OtherWorkspaceViewportWidth = e.NewSize.Width;
            viewModel.OtherWorkspaceViewportHeight = e.NewSize.Height;
        }
    }

    private sealed record DeferredNavigationItem(
        string LabelPath,
        string Tag,
        FASymbol Symbol);
}
