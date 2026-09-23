using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IwashiScope.App.Wpf.Controls;

/// <summary>A transient press, never a preference. Capture handles release outside the button.</summary>
public sealed class ChromaticityReferenceButton : Button
{
    public bool IsReferencePressed { get; private set; }
    public event EventHandler? ReferencePressedChanged;
    public ChromaticityReferenceButton()
    {
        IsEnabledChanged += (_, _) => { if (!IsEnabled) ResetReference(); };
        Unloaded += (_, _) => ResetReference();
    }
    private void SetPressed(bool value)
    {
        value &= IsEnabled;
        if (IsReferencePressed == value) return;
        IsReferencePressed = value;
        ReferencePressedChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ResetReference()
    {
        SetPressed(false);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsEnabled) { Focus(); if (CaptureMouse()) SetPressed(true); }
        e.Handled = true;
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e) { ResetReference(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { SetPressed(false); base.OnLostMouseCapture(e); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) { SetPressed(true); e.Handled = true; }
        else if (e.Key == Key.Escape) { ResetReference(); e.Handled = true; }
        else base.OnPreviewKeyDown(e);
    }
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) { ResetReference(); e.Handled = true; }
        else base.OnPreviewKeyUp(e);
    }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { ResetReference(); base.OnLostKeyboardFocus(e); }
}
