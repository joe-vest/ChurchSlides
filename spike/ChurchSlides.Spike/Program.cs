using System.Windows;

namespace ChurchSlides.Spike;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Startup.MainEntered(args);
        Startup.StartKestrelIfRequested();

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "Spike error");
            e.Handled = true;
        };
        app.Exit += (_, _) =>
        {
            if (Startup.KestrelRequested)
                KestrelHost.Stop();
        };
        app.Run(new MainWindow());
    }
}
