using System;
using System.Windows.Forms;

namespace MatrixScreensaver;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string mode = "/s";
        IntPtr previewHandle = IntPtr.Zero;

        if (args.Length > 0)
        {
            string a = args[0].ToLowerInvariant().Trim();
            if (a.StartsWith("/c")) mode = "/c";
            else if (a.StartsWith("/p"))
            {
                mode = "/p";
                string hwndStr = a.Length > 2 ? a.Substring(3) : (args.Length > 1 ? args[1] : "");
                if (long.TryParse(hwndStr, out long h)) previewHandle = new IntPtr(h);
            }
            else if (a.StartsWith("/s")) mode = "/s";
        }

        switch (mode)
        {
            case "/c":
                MessageBox.Show(
                    "Matrix Screensaver\n\nDigital rain, just like the movie.\nNo configuration options.",
                    "Matrix Screensaver",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;

            case "/p":
                if (previewHandle != IntPtr.Zero)
                {
                    Application.Run(new MatrixForm(previewHandle));
                }
                return;

            case "/s":
            default:
                foreach (Screen screen in Screen.AllScreens)
                {
                    var f = new MatrixForm(screen.Bounds);
                    f.Show();
                }
                Application.Run();
                return;
        }
    }
}
