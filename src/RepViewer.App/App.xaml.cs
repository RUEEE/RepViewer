using System.IO;
using System.Windows;

namespace RepViewer.App;
public partial class App : Application
{
    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) UiScaleService.Apply(window);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            var smokeIndex = Array.FindIndex(e.Args, argument => argument.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
            var success = smokeIndex + 1 >= e.Args.Length || window.OpenReplay(e.Args[smokeIndex + 1], false);
            window.Close();
            Shutdown(success ? 0 : 1);
            return;
        }
        if (e.Args.FirstOrDefault(argument => File.Exists(argument)) is { } replay) window.OpenReplay(replay);
        MainWindow = window;
        window.Loaded += (_, _) => window.HandleAssociationStartup();
        window.Show();
    }
}
