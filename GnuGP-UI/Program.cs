using System;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows;

namespace GpgUi
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            ThemeHelper.ApplyGlobalTheme(app);

            string startupFile = (args != null && args.Length > 0) ? args[0] : null;
            var window = new MainWindow(startupFile);
            app.Run(window);
        }
    }
}
