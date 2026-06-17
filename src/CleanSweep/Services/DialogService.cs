using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CleanSweep.Services;

/// <summary>Modal confirmations and pickers for the view models, decoupled from the views.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool destructive = false);

    /// <summary>Native folder picker; returns the chosen local path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}

public sealed class DialogService : IDialogService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool destructive = false)
    {
        var owner = MainWindow;
        if (owner is null) return false; // no UI (e.g. design time) - never auto-confirm a destructive action
        return await new ConfirmWindow(title, message, confirmText, destructive).ShowDialog<bool>(owner);
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var owner = MainWindow;
        if (owner is null) return null;
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

/// <summary>A minimal themed yes/no dialog, built in code so it inherits the app theme.</summary>
internal sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText, bool destructive)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8)
        };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };

        var cancel = new Button { Content = "Cancel", MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close(false);

        var confirm = new Button { Content = confirmText, MinWidth = 92, IsDefault = true };
        confirm.Classes.Add("accent");
        if (destructive) confirm.Classes.Add("danger");
        confirm.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { cancel, confirm }
        };

        cancel.IsCancel = true;
        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel { Children = { heading, body, buttons } }
        };
    }
}
