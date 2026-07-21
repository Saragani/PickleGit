using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PickleGit.Services
{
    /// <summary>Applies (or removes) the Windows 10/11 dark title bar via DWM for a given window.
    /// Shared so both MainWindow's startup call and a live theme switch (App.ApplyTheme) can
    /// re-theme every currently open window's chrome, not just the client area.</summary>
    public static class TitleBarTheme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void Apply(Window window, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref value, Marshal.SizeOf(value));
        }
    }
}
