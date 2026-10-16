namespace WindowsDesktopMover;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--smoke-test")) return SmokeTest.Run();
        if (args.Contains("--desktop-api-test")) return SmokeTest.Run(testKeyboard: false);

        using var instance = new Mutex(true, @"Local\WindowsDesktopMover", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Desktop Mover is already running. Look for its icon in the system tray.", "Desktop Mover");
            return 0;
        }
        try
        {
            using var context = new TrayContext();
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Desktop Mover could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
