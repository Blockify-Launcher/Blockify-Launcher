using System.Windows;
using WpfScreenHelper;

namespace BlockifyLauncher.Core.ResizeForm
{
    /// <summary>
    /// Remembers the last known location of a fully custom wpf window 
    /// for minimizing and returning window to its original location.
    /// </summary>
    public static class WindowStateHelper
    {
        private static double Top { get; set; }
        private static double Left { get; set; }
        private static double Width { get; set; }
        private static double Height { get; set; }

        // Required because using WindowState.Maximized will not respect the WorkingArea of the screen in a fully custom window
        public static bool IsMaximized { get; private set; }
        // Blocks the window from running OnSizeChanged when resizing the window
        public static bool BlockStateChange { get; set; }

        private static void SetWindowTop(Window window)
        {
            BlockStateChange = true;
            window.Top = Top;
        }

        private static void SetWindowLeft(Window window)
        {
            BlockStateChange = true;
            window.Left = Left;
        }

        private static void SetWindowWidth(Window window)
        {
            BlockStateChange = true;
            window.Width = Width;
        }

        private static void SetWindowHeight(Window window)
        {
            BlockStateChange = true;
            window.Height = Height;
        }

        public static void UpdateLastKnownLocation(double top, double left)
        {
            Top = top;
            Left = left;
        }

        public static void UpdateLastKnownNormalSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public static void SetWindowMaximized(Window window)
        {
            IsMaximized = true;
            window.WindowState = WindowState.Normal;
        }

        // Returns a percentage which is how far the mouse pointer is from the left of the window
        private static double MousePercentageFromLeft(Window window)
        {
            var mouseMinusZeroToLeft = MouseHelper.MousePosition.X - window.Left;
            var percentage = mouseMinusZeroToLeft / window.Width;
            return percentage;
        }

        // Returns the window to its last known size and location before it was maximized.
        // When useMouseLocation = true (dragging away from maximized) then the location is below the
        // mouse pointer, respecting the percentage the pointer is from the left of the window
        public static void SetWindowSizeToNormal(Window window, bool useMouseLocation = false)
        {
            IsMaximized = false;

            var percentage = MousePercentageFromLeft(window);

            SetWindowWidth(window);
            SetWindowHeight(window);

            if (useMouseLocation)
            {
                Top = MouseHelper.MousePosition.Y;

                var valueOnNewSize = percentage * Width;
                Left = MouseHelper.MousePosition.X - valueOnNewSize;
            }

            SetWindowTop(window);
            SetWindowLeft(window);
        }
    }
}
