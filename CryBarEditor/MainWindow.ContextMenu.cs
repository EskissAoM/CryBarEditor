using Avalonia.Controls;
using Avalonia.Input.Platform;
using CryBar.Bar;
using CryBarEditor.Classes;
using CryBarEditor.Windows;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CryBarEditor;

public partial class MainWindow
{
    #region Context Menu Helpers
    /// <summary>
    /// Resolves the ListBox that owns this context menu item.
    /// </summary>
    ListBox? GetContextListBox(object? sender)
    {
        var item = sender as MenuItem;
        return item?.Parent?.Parent?.Parent as ListBox;
    }

    /// <summary>
    /// Returns whether the context menu was opened from the BAR entries list (vs Root files list).
    /// </summary>
    bool IsContextFromBAR(ListBox list) => list.ItemsSource == BarEntries;

    /// <summary>
    /// Gets the full relative path for the currently selected entry, regardless of which list it's in.
    /// Returns null if no valid selection exists.
    /// </summary>
    string? GetContextSelectedRelativePath(ListBox list)
    {
        if (IsContextFromBAR(list))
        {
            if (SelectedBarEntry == null) return null;
            return GetBARFullRelativePath(SelectedBarEntry);
        }
        else
        {
            if (SelectedRootFileEntry == null) return null;
            return GetRootFullRelativePath(SelectedRootFileEntry);
        }
    }

    /// <summary>
    /// Gets the display name of the currently selected entry.
    /// </summary>
    string? GetContextSelectedName(ListBox list)
    {
        if (IsContextFromBAR(list))
            return SelectedBarEntry?.Name;
        return SelectedRootFileEntry?.Name;
    }

    /// <summary>
    /// Builds ExportFileInfo list from the current selection in the given ListBox.
    /// </summary>
    List<ExportFileInfo> GetContextSelectedExportFiles(ListBox list)
    {
        if (IsContextFromBAR(list))
        {
            return SelectedBarFileEntries.Select(e => new ExportFileInfo
            {
                RelativePath = e.RelativePath,
                FullRelativePath = GetBARFullRelativePath(e),
                IsCompressed = e.IsCompressed
            }).ToList();
        }

        return SelectedRootFileEntries.Select(e => new ExportFileInfo
        {
            RelativePath = e.RelativePath,
            FullRelativePath = GetRootFullRelativePath(e),
            IsCompressed = false // root files don't have a compression flag
        }).ToList();
    }
    #endregion

    #region ContextMenu events
    void MenuItem_CopyFileName(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var list = GetContextListBox(sender);
        if (list == null) return;

        var name = GetContextSelectedName(list);
        if (name != null) Clipboard?.SetTextAsync(name);
    }

    void MenuItem_CopyFilePath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var list = GetContextListBox(sender);
        if (list == null) return;

        var path = GetContextSelectedRelativePath(list);
        if (path != null) Clipboard?.SetTextAsync(path);
    }

    void MenuItem_ExportSelectedOpenDirectory(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Directory.Exists(_exportRootDirectory))
            return;

        var list = GetContextListBox(sender);
        if (list == null) return;

        var relative_path_full = GetContextSelectedRelativePath(list);
        if (relative_path_full == null) return;

        var export_path = Path.Combine(_exportRootDirectory, relative_path_full);
        var export_dir = Path.GetDirectoryName(export_path);
        if (!string.IsNullOrEmpty(export_dir))
        {
            Directory.CreateDirectory(export_dir);
            OpenDirectoryInExplorer(export_dir);
        }
    }

    CancellationTokenSource? bank_play_csc = null;
    IBankItem? _currentlyPlaying = null;

    void BankItem_Play(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PlaySelectedBankItem();

    void BankEntryList_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => PlaySelectedBankItem();

    void PlaySelectedBankItem()
    {
        if (SelectedBankEntry != null && FmodBank != null)
            PlayBankItem(SelectedBankEntry);
    }

    void BankContextMenu_Opened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _selectedBankCount = SelectedBankEntries.Count;
        OnPropertyChanged(nameof(SelectedBankSingle));
        OnPropertyChanged(nameof(SelectedBankMultiple));
        OnPropertyChanged(nameof(SelectedBankIsEventSingle));
        OnPropertyChanged(nameof(HasExportDirectory));
    }

    // Click on the type icon (music note / waveform) of a row plays that entry.
    void BankItem_IconPlay(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is IBankItem item && FmodBank != null)
            PlayBankItem(item);
    }

    // Click on the Stop icon shown on the currently-playing row.
    void BankItem_StopPlayback(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        StopBankPlayback();
    }

    async void PlayBankItem(IBankItem item)
    {
        // Only one entry plays at a time; stop whatever is playing first (clears its icon).
        StopBankPlayback();

        var csc = new CancellationTokenSource();
        bank_play_csc = csc;

        _currentlyPlaying = item;
        item.IsPlaying = true;
        try
        {
            await item.Play(csc.Token);
        }
        catch { /* cancelled or playback failed */ }
        finally
        {
            // A superseding play (incl. re-playing the same item) installs its own csc;
            // only the still-current invocation may clear shared state. Keying on `item`
            // identity breaks when the same item is replayed.
            if (ReferenceEquals(bank_play_csc, csc))
            {
                item.IsPlaying = false;
                _currentlyPlaying = null;
                bank_play_csc = null;
            }

            csc.Dispose();
        }
    }

    void StopBankPlayback()
    {
        // Don't dispose here - the owning PlayBankItem disposes its own csc once its
        // awaited Play unwinds; disposing now would race that still-running task.
        bank_play_csc?.Cancel();
        bank_play_csc = null;

        if (_currentlyPlaying != null)
        {
            _currentlyPlaying.IsPlaying = false;
            _currentlyPlaying = null;
        }
    }

    void MenuItem_OpenExportedInEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var list = GetContextListBox(sender);
        if (list == null) return;

        var relPath = GetContextSelectedRelativePath(list);
        if (relPath == null) return;

        var exportPath = ResolveExportedFilePath(relPath);
        if (exportPath != null)
            TryLaunchEditorForFile(exportPath);
    }

    bool _contextMenuIsFromBAR;

    /// <summary>Binary extensions that cannot be parsed for text-based dependency references.</summary>
    static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".BAR", ".blob", ".ddt", ".wav", ".mp3", ".ogg", ".fnt", ".ttf", ".otf",
        ".cur", ".ico", ".bmp", ".png", ".jpg", ".jpeg", ".gif", ".tga", ".psd",
        ".exe", ".dll", ".pdb", ".data", ".tma",
    };

    public bool CanShowDependencies
    {
        get
        {
            if (ContextSelectedItemsCount != 1) return false;
            var relPath = _contextMenuIsFromBAR
                ? SelectedBarEntry?.RelativePath ?? ""
                : SelectedRootFileEntry?.RelativePath ?? "";
            var ext = Path.GetExtension(relPath);
            // .bank files are binary but we handle them by redirecting to their soundset file
            if (ext.Equals(".bank", StringComparison.OrdinalIgnoreCase)) return true;
            // .tmm files are binary but have known dependencies
            if (ext.Equals(".tmm", StringComparison.OrdinalIgnoreCase)) return true;
            return !BinaryExtensions.Contains(ext);
        }
    }

    public bool CanOpenRootDirectory =>
        !_contextMenuIsFromBAR && SelectedRootFileEntry != null && Directory.Exists(_rootDirectory);

    void MenuItem_OpenRootDirectory(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedRootFileEntry == null || string.IsNullOrEmpty(_rootDirectory)) return;
        var fullPath = Path.Combine(_rootDirectory, SelectedRootFileEntry.RelativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            OpenDirectoryInExplorer(dir);
    }

    void ContextMenu_Opened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listbox = (ListBox)((ContextMenu)sender!).Parent!.Parent!;
        _contextMenuIsFromBAR = IsContextFromBAR(listbox);
        ContextSelectedItemsCount = listbox.SelectedItems!.Count;
        OnPropertyChanged(nameof(CanOpenInEditor));
        OnPropertyChanged(nameof(CanToggleQuickAccess));
        OnPropertyChanged(nameof(IsInQuickAccess));
        OnPropertyChanged(nameof(QuickAccessToggleText));
        OnPropertyChanged(nameof(CanShowDependencies));
        OnPropertyChanged(nameof(CanOpenRootDirectory));
    }
    #endregion
}
