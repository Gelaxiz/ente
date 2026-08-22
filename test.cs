using System;
using System.Text;
using Microsoft.UI.Xaml.Input;
using Windows.System;

public class Test {
    public void GlobalShortcutBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        
        var key = e.Key;
        // Ignore standalone modifiers
        if (key == VirtualKey.Control || key == VirtualKey.LeftControl || key == VirtualKey.RightControl ||
            key == VirtualKey.Shift || key == VirtualKey.LeftShift || key == VirtualKey.RightShift ||
            key == VirtualKey.Menu || key == VirtualKey.LeftMenu || key == VirtualKey.RightMenu ||
            key == VirtualKey.Windows || key == VirtualKey.LeftWindows || key == VirtualKey.RightWindows)
            return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var win = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
                  Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        var sb = new StringBuilder();
        if (ctrl) sb.Append("Ctrl+");
        if (shift) sb.Append("Shift+");
        if (alt) sb.Append("Alt+");
        if (win) sb.Append("Win+");
        
        sb.Append(key.ToString());
        
        // Output sb.ToString();
    }
}
