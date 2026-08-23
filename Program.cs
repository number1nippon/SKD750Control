using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SKD750Control
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Global exception handlers to prevent silent crashes and log details
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    var msg = e.Exception?.ToString() ?? "Unknown UI thread exception";
                    var logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                    using (var writer = new System.IO.StreamWriter(logFilePath, true))
                    {
                        writer.WriteLine($"{DateTime.Now}: Unhandled UI exception");
                        writer.WriteLine(msg);
                        writer.WriteLine();
                    }
                }
                catch { }
                MessageBox.Show($"Unexpected error: {e.Exception?.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    var msg = ex?.ToString() ?? "Unknown unhandled exception";
                    var logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                    using (var writer = new System.IO.StreamWriter(logFilePath, true))
                    {
                        writer.WriteLine($"{DateTime.Now}: Unhandled domain exception");
                        writer.WriteLine(msg);
                        writer.WriteLine();
                    }
                }
                catch { }
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Show the connection chooser BEFORE the main form/window even exists.
            // The direct-USB NikonManager's native scheduler must be constructed from a
            // clean, top-level call (mirroring the pre-refactor version, which built
            // NikonManager directly inside the MainForm constructor with no dialog message
            // pump involved at all). Showing/closing a modal dialog on top of an already-
            // running MainForm and then constructing NikonManager as that dialog unwinds
            // put construction on a different/nested call stack and crashed natively.
            bool useDdServer = false;
            string ddHost = null;
            int ddPort = 0;
            using (var chooser = new ConnectionChooserDialog())
            {
                if (chooser.ShowDialog() != DialogResult.OK)
                    return;

                useDdServer = chooser.UseDdServer;
                ddHost = chooser.Host;
                ddPort = chooser.Port;
            }

            Application.Run(new MainForm(useDdServer, ddHost, ddPort));
        }

    }
}
