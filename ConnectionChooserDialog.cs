using System;
using System.Windows.Forms;

namespace SKD750Control
{
    /// <summary>
    /// Simple modal dialog letting the user choose between direct USB connection
    /// or a network ddserver (DslrDashboardServer) bridge, entering host:port
    /// for the latter.
    /// </summary>
    public class ConnectionChooserDialog : Form
    {
        private RadioButton radioUsb;
        private RadioButton radioDdServer;
        private TextBox textHost;
        private NumericUpDown numericPort;
        private Button buttonOk;
        private Button buttonCancel;

        public bool UseDdServer => radioDdServer.Checked;
        public string Host => textHost.Text.Trim();
        public int Port => (int)numericPort.Value;

        public ConnectionChooserDialog()
        {
            Text = "Camera Connection";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(340, 190);

            radioUsb = new RadioButton
            {
                Text = "Direct USB (Nikon MAID SDK)",
                Location = new System.Drawing.Point(15, 15),
                AutoSize = true,
                Checked = true
            };
            radioDdServer = new RadioButton
            {
                Text = "ddserver (DslrDashboardServer over network)",
                Location = new System.Drawing.Point(15, 45),
                AutoSize = true
            };

            var labelHost = new Label { Text = "Host:", Location = new System.Drawing.Point(35, 80), AutoSize = true };
            textHost = new TextBox { Location = new System.Drawing.Point(90, 77), Width = 150, Text = "192.168.1.1" };

            var labelPort = new Label { Text = "Port:", Location = new System.Drawing.Point(35, 110), AutoSize = true };
            numericPort = new NumericUpDown { Location = new System.Drawing.Point(90, 107), Width = 80, Minimum = 1, Maximum = 65535, Value = 4757 };

            buttonOk = new Button { Text = "Connect", Location = new System.Drawing.Point(150, 145), DialogResult = DialogResult.OK };
            buttonCancel = new Button { Text = "Cancel", Location = new System.Drawing.Point(240, 145), DialogResult = DialogResult.Cancel };

            AcceptButton = buttonOk;
            CancelButton = buttonCancel;

            Controls.Add(radioUsb);
            Controls.Add(radioDdServer);
            Controls.Add(labelHost);
            Controls.Add(textHost);
            Controls.Add(labelPort);
            Controls.Add(numericPort);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
        }
    }
}
