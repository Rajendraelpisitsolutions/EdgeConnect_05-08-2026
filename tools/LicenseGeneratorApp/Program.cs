using System;
using System.Windows.Forms;

namespace ElpisEdgeConnect.LicenseGeneratorApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Create the license-history database up front so any path/permission
        // problem surfaces at launch rather than on the first Generate click.
        try
        {
            LicenseDatabase.EnsureCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open the license history database:\n\n" + ex.Message
                    + "\n\nDatabase: " + LicenseDatabase.DatabasePath
                    + "\n\nLicenses can still be generated; they just won't be recorded.",
                "Database warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Application.Run(new MainForm());
    }
}
