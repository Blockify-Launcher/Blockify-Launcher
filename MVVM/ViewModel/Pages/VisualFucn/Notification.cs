using System.Windows;

namespace BlockifyLauncher.MVVM.ViewModel.Pages.VisualFucn
{
    public class Notification
    {
        private static MainWindow mainWindow
        {
            get => (MainWindow)Application.Current.MainWindow;
        }

        public static void Show(NotificationStruct ns)
        {
            if (ns.Image == null)
                _ = mainWindow.NotificationElement.GetNotification(ns.Title, ns.Description, ns.Delay);
            else
                _ = mainWindow.NotificationElement.GetNotification(ns.Title, ns.Description, ns.Image, ns.Delay);
        }

        public static void Show(string title, string description, string image = null, int delay = 1000)
        {
            if (image == null)
                _ = mainWindow.NotificationElement.GetNotification(title, description, delay);
            else
                _ = mainWindow.NotificationElement.GetNotification(title, description, image, delay);
        }
    }
}
