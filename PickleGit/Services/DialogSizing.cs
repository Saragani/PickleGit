using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace PickleGit.Services
{
    /// <summary>
    /// Shared MaxHeight policy for modal dialogs (ConfirmDialog, ErrorDialog, TextPromptDialog)
    /// that use SizeToContent="Height" — caps growth to the usable work area of the monitor the
    /// dialog will actually appear on, so a long bound message can't push its button row off-screen.
    /// </summary>
    internal static class DialogSizing
    {
        private const double BottomMargin = 40;

        /// <summary>
        /// Caps at <paramref name="owner"/>'s monitor work area (matching
        /// WindowStartupLocation="CenterOwner", which centers on the owner's monitor), falling
        /// back to the primary display's work area when there is no owner. SystemParameters.WorkArea
        /// alone always reports the primary monitor, which is wrong whenever the owner sits on a
        /// secondary display of a different size.
        /// </summary>
        public static double ForOwner(Window owner)
        {
            if (owner == null)
                return SystemParameters.WorkArea.Height - BottomMargin;

            try
            {
                var handle = new WindowInteropHelper(owner).Handle;
                if (handle == IntPtr.Zero)
                    return SystemParameters.WorkArea.Height - BottomMargin;

                var workAreaPx = Screen.FromHandle(handle).WorkingArea;
                var dpiScaleY = VisualTreeHelper.GetDpi(owner).DpiScaleY;
                return workAreaPx.Height / dpiScaleY - BottomMargin;
            }
            catch
            {
                return SystemParameters.WorkArea.Height - BottomMargin;
            }
        }
    }
}
