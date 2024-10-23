using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BlockifyLauncher.MVVM.Views.Style
{
    public partial class ApplicationStyle : ResourceDictionary
    {
        #region Resize mainWindow Form
        bool ResizeInProcess = true;

        private void Resize_Init(object sender, DragStartedEventArgs e)
        {
            if (sender is Thumb senderThumb)
            {
                ResizeInProcess = true;
                senderThumb.CaptureMouse();
            }
        }

        private void Resize_End(object sender, DragCompletedEventArgs e)
        {
            if (sender is Thumb senderThumb)
            {
                ResizeInProcess = false;
                senderThumb.ReleaseMouseCapture();
            }
        }

        private void Resizeing_Form(object sender, DragDeltaEventArgs e)
        {
            if (ResizeInProcess)
            {
                double temp = 0;
                if (sender is Thumb senderThumb)
                {
                    Window mainWindow = senderThumb.Tag as Window;
                    if (senderThumb != null)
                    {
                        double width = e.HorizontalChange + mainWindow.ActualWidth; // width.
                        double height = e.VerticalChange + mainWindow.ActualHeight; // height.
                        if (senderThumb.Name.Contains("right", StringComparison.OrdinalIgnoreCase))
                        {
                            width += 1;
                            if (width > 0)
                                mainWindow.Width = width;
                        }
                        if (senderThumb.Name.Contains("left", StringComparison.OrdinalIgnoreCase))
                        {
                            width -= 5;
                            mainWindow.Left += width;
                            width = mainWindow.Width - width;
                            if (width > 0)
                            {
                                mainWindow.Width = width;
                            }
                        }
                        if (senderThumb.Name.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                        {
                            height += 5;
                            if (height > 0)
                                mainWindow.Height = height;
                        }
                    }
                }

            }

            /*if (ResizeInProcess)
            {
                Thumb senderThumb = (Thumb)sender;
                Window mainWindow = senderThumb.Tag as Window;
                if (mainWindow != null)
                {
                    double width = e.HorizontalChange + mainWindow.ActualWidth;
                    double height = e.VerticalChange + mainWindow.ActualHeight;
                    if (senderThumb.Name.ToLower().Contains("right"))
                    {
                        if (width > 0) mainWindow.Width = width;
                    }
                    if (senderThumb.Name.ToLower().Contains("left"))
                    {
                        if (width > 0)
                        {
                            double left = mainWindow.Left + e.HorizontalChange;
                            mainWindow.Width = width;
                            mainWindow.Left = left;
                        }
                    }
                    if (senderThumb.Name.ToLower().Contains("bottom"))
                    {
                        if (height > 0) mainWindow.Height = height;
                    }
                }
            }*/
        }

        #endregion
    }
}
