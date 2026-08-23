namespace DwgTrueView.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(
                e.Exception.Message,
                "Unexpected error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        var form = new MainForm();
        if (args.FirstOrDefault() is { } path)
        {
            form.Shown += async (_, _) => await form.OpenPathAsync(path);
        }
        Application.Run(form);
    }
}