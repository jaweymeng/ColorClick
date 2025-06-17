using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ColorClickApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            System.IO.File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] Unhandled exception: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
            // Prevent the application from crashing
            e.Handled = true;
            System.Windows.MessageBox.Show("An unhandled exception occurred. Please check crash_log.txt for details.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}

