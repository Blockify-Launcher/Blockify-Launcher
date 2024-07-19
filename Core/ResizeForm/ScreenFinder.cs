using System.Windows;
using System.Windows.Forms;

namespace BlockifyLauncher.Core.ResizeForm
{
    /// <summary>
    /// Used when maximizing a fully custom window to determine which screen the window should be maximized to. 
    /// </summary>
    public static class ScreenFinder
    {

        public static Screen FindAppropriateScreen(Window window)
        {
            var windowRight = window.Left + window.Width;
            var windowBottom = window.Top + window.Height;

            var allScreens = Screen.AllScreens.ToList();

            // If the window is inside all of a single screen boundaries, maximize to that
            var screenInsideAllBounds = allScreens.Find(x => window.Top >= x.Bounds.Top
                                                        && window.Left >= x.Bounds.Left
                                                        && windowRight <= x.Bounds.Right
                                                        && windowBottom <= x.Bounds.Bottom);
            if (screenInsideAllBounds != null)
            {
                return screenInsideAllBounds;
            }

            // Failing the above (between two screens in side-by-side configuration)
            // Measure if the window is between the top and bottom of any screens.
            // Then measure the percentage it is within each screen and pick a winner
            var screensInBounds = allScreens.FindAll(x => window.Top >= x.Bounds.Top
                                                    && windowBottom <= x.Bounds.Bottom);
            if (screensInBounds.Count > 0)
            {
                var values = new List<Tuple<double, Screen>>();
                // Determine the amount of width inside each screen
                foreach (var screen in screensInBounds.OrderBy(x => x.Bounds.Left))
                {
                    // This has only been tested in a two screen, side-by-side setup.
                    double amountInScreen;
                    if (screen.Bounds.Left == 0)
                    {
                        var rightOfWindow = window.Left + window.Width;
                        var outsideRightBoundary = rightOfWindow - screen.Bounds.Right;
                        amountInScreen = window.Width - outsideRightBoundary;
                        values.Add(new Tuple<double, Screen>(amountInScreen, screen));
                    }
                    else
                    {
                        var outsideLeftBoundary = screen.Bounds.Left - window.Left;
                        amountInScreen = window.Width - outsideLeftBoundary;
                        values.Add(new Tuple<double, Screen>(amountInScreen, screen));
                    }
                }

                values = values.OrderByDescending(x => x.Item1).ToList();
                if (values.Count > 0)
                {
                    return values[0].Item2;
                }
            }

            // Failing all else
            return Screen.PrimaryScreen;
        }
    }
}
