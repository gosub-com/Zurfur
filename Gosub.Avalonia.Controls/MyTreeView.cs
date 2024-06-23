using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Gosub.Avalonia.Controls
{

    /// <summary>
    /// Select items on tap instead of on press so mobile devices can drag without selecting
    /// </summary>
    public class MyTreeView : TreeView
    {
        protected override Type StyleKeyOverride => typeof(TreeView);

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            Tapped += this_Tapped;
        }

        /// <summary>
        /// Select item via tapping
        /// </summary>
        private void this_Tapped(object? sender, TappedEventArgs e)
        {
            // Forward tap to what's in base.OnPointerPressed
            if (e.Source is Visual)
            {
                var keymap = Application.Current!.PlatformSettings!.HotkeyConfiguration;
                e.Handled = UpdateSelectionFromEventSource(
                    e.Source,
                    true,
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                    e.KeyModifiers.HasFlag(keymap.CommandModifiers),
                    false //point.Properties.IsRightButtonPressed);
                );
            }
        }

        /// <summary>
        /// Disable select item behavior (use tap instead)
        /// </summary>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
        }
    }
}
