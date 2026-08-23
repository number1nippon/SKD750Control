using System;
using System.Drawing;
using System.Windows.Forms;

namespace SKD750Control
{
    public sealed class ZoomPreviewForm : Form
    {
        private readonly PictureBox pb;

        public ZoomPreviewForm()
        {
            Text = "Zoom";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            TopMost = false; // stay above owner only (owned window)
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            pb = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Black
            };
            Controls.Add(pb);
            ClientSize = new Size(320, 320);
        }

        public void ShowImage(Image img)
        {
            var old = pb.Image;
            pb.Image = img;
            old?.Dispose();
            if (!Visible) Show();
        }

        // Prevent this tool window from stealing focus when shown
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }
}
