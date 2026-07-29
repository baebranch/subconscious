using Avalonia.Controls;
using Avalonia.Input;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        var composer = this.FindControl<TextBox>("ComposerBox");
        if (composer is not null)
        {
            composer.KeyDown += OnComposerKeyDown;
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }
        if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
