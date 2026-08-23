namespace SKD750Control
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">True if managed resources should be disposed; otherwise, False.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.pictureBox = new System.Windows.Forms.PictureBox();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.label_name = new System.Windows.Forms.Label();
            this.button_capture = new System.Windows.Forms.Button();
            this.button_toggleliveview = new System.Windows.Forms.Button();
            this.button_autofocus = new System.Windows.Forms.Button();
            this.label_afMode = new System.Windows.Forms.Label();
            this.comboBox_afMode = new System.Windows.Forms.ComboBox();
            this.label_iso = new System.Windows.Forms.Label();
            this.button_iso_minus = new System.Windows.Forms.Button();
            this.button_iso_plus = new System.Windows.Forms.Button();
            this.label_iso_value = new System.Windows.Forms.Label();
            this.label_aperture = new System.Windows.Forms.Label();
            this.button_aperture_minus = new System.Windows.Forms.Button();
            this.button_aperture_plus = new System.Windows.Forms.Button();
            this.label_aperture_value = new System.Windows.Forms.Label();
            this.label_shutter = new System.Windows.Forms.Label();
            this.button_shutter_minus = new System.Windows.Forms.Button();
            this.button_shutter_plus = new System.Windows.Forms.Button();
            this.label_shutter_value = new System.Windows.Forms.Label();
            this.button_exposurePreview = new System.Windows.Forms.Button();
            this.button_meteringMode = new System.Windows.Forms.Button();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.tableLayoutPanel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.pictureBox, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.tableLayoutPanel2, 0, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 1;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(800, 600); // Increased window size
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // pictureBox
            // 
            this.pictureBox.BackColor = System.Drawing.SystemColors.ControlDark;
            this.pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox.Location = new System.Drawing.Point(153, 3);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(644, 594); // Adjusted picture box size
            this.pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox.TabIndex = 0;
            this.pictureBox.TabStop = false;
            this.pictureBox.MouseClick += new System.Windows.Forms.MouseEventHandler(this.pictureBox_MouseClick);
            this.pictureBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox_MouseDown);
            this.pictureBox.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pictureBox_MouseUp);
            // 
            // tableLayoutPanel2
            // 
            this.tableLayoutPanel2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanel2.ColumnCount = 3;
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 25F));
            this.tableLayoutPanel2.Controls.Add(this.label_name, 0, 0);
            this.tableLayoutPanel2.Controls.Add(this.button_capture, 0, 1);
            this.tableLayoutPanel2.Controls.Add(this.button_toggleliveview, 0, 2);
            this.tableLayoutPanel2.Controls.Add(this.button_autofocus, 0, 3);
            this.tableLayoutPanel2.Controls.Add(this.label_afMode, 0, 5);
            this.tableLayoutPanel2.Controls.Add(this.comboBox_afMode, 0, 6);
            this.tableLayoutPanel2.Controls.Add(this.label_iso, 0, 7);
            this.tableLayoutPanel2.Controls.Add(this.button_iso_minus, 1, 7);
            this.tableLayoutPanel2.Controls.Add(this.button_iso_plus, 2, 7);
            this.tableLayoutPanel2.Controls.Add(this.label_iso_value, 0, 8);
            this.tableLayoutPanel2.Controls.Add(this.label_aperture, 0, 9);
            this.tableLayoutPanel2.Controls.Add(this.button_aperture_minus, 1, 9);
            this.tableLayoutPanel2.Controls.Add(this.button_aperture_plus, 2, 9);
            this.tableLayoutPanel2.Controls.Add(this.label_aperture_value, 0, 10);
            this.tableLayoutPanel2.Controls.Add(this.label_shutter, 0, 11);
            this.tableLayoutPanel2.Controls.Add(this.button_shutter_minus, 1, 11);
            this.tableLayoutPanel2.Controls.Add(this.button_shutter_plus, 2, 11);
            this.tableLayoutPanel2.Controls.Add(this.label_shutter_value, 0, 12);
            this.tableLayoutPanel2.Controls.Add(this.button_exposurePreview, 0, 13);
            this.tableLayoutPanel2.Controls.Add(this.button_meteringMode, 0, 14);
            this.tableLayoutPanel2.Location = new System.Drawing.Point(3, 3);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 15;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(144, 594);
            this.tableLayoutPanel2.TabIndex = 1;
            // 
            // label_name
            // 
            this.label_name.AutoSize = true;
            this.label_name.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_name.Location = new System.Drawing.Point(3, 0);
            this.label_name.Name = "label_name";
            this.label_name.Size = new System.Drawing.Size(138, 40);
            this.label_name.TabIndex = 0;
            this.label_name.Text = "No Camera";
            this.label_name.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // button_capture
            // 
            this.button_capture.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_capture.Location = new System.Drawing.Point(3, 43);
            this.button_capture.Name = "button_capture";
            this.button_capture.Size = new System.Drawing.Size(138, 34);
            this.button_capture.TabIndex = 1;
            this.button_capture.Text = "Capture";
            this.button_capture.UseVisualStyleBackColor = true;
            this.button_capture.Click += new System.EventHandler(this.button_capture_Click);
            // 
            // button_toggleliveview
            // 
            this.button_toggleliveview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_toggleliveview.Location = new System.Drawing.Point(3, 83);
            this.button_toggleliveview.Name = "button_toggleliveview";
            this.button_toggleliveview.Size = new System.Drawing.Size(138, 34);
            this.button_toggleliveview.TabIndex = 2;
            this.button_toggleliveview.Text = "Toggle Live View";
            this.button_toggleliveview.UseVisualStyleBackColor = true;
            this.button_toggleliveview.Click += new System.EventHandler(this.button_toggleliveview_Click);
            // 
            // button_autofocus
            // 
            this.button_autofocus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_autofocus.Location = new System.Drawing.Point(3, 123);
            this.button_autofocus.Name = "button_autofocus";
            this.button_autofocus.Size = new System.Drawing.Size(138, 34);
            this.button_autofocus.TabIndex = 3;
            this.button_autofocus.Text = "Autofocus";
            this.button_autofocus.UseVisualStyleBackColor = true;
            this.button_autofocus.Click += new System.EventHandler(this.button_autofocus_Click);
            // 
            // label_afMode
            // 
            this.label_afMode.AutoSize = true;
            this.tableLayoutPanel2.SetColumnSpan(this.label_afMode, 3);
            this.label_afMode.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_afMode.Location = new System.Drawing.Point(3, 203);
            this.label_afMode.Name = "label_afMode";
            this.label_afMode.Size = new System.Drawing.Size(138, 20);
            this.label_afMode.TabIndex = 17;
            this.label_afMode.Text = "AF Mode (LV):";
            this.label_afMode.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // comboBox_afMode
            // 
            this.comboBox_afMode.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel2.SetColumnSpan(this.comboBox_afMode, 3);
            this.comboBox_afMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_afMode.FormattingEnabled = true;
            this.comboBox_afMode.Items.AddRange(new object[] {"AF-S", "AF-C"});
            this.comboBox_afMode.Location = new System.Drawing.Point(3, 226);
            this.comboBox_afMode.Name = "comboBox_afMode";
            this.comboBox_afMode.Size = new System.Drawing.Size(138, 21);
            this.comboBox_afMode.TabIndex = 18;
            this.comboBox_afMode.SelectedIndexChanged += new System.EventHandler(this.comboBox_afMode_SelectedIndexChanged);
            // 
            // label_iso
            // 
            this.label_iso.AutoSize = true;
            this.label_iso.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_iso.Location = new System.Drawing.Point(3, 160);
            this.label_iso.Name = "label_iso";
            this.label_iso.Size = new System.Drawing.Size(72, 40);
            this.label_iso.TabIndex = 4;
            this.label_iso.Text = "ISO";
            this.label_iso.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // button_iso_minus
            // 
            this.button_iso_minus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_iso_minus.Location = new System.Drawing.Point(78, 160);
            this.button_iso_minus.Name = "button_iso_minus";
            this.button_iso_minus.Size = new System.Drawing.Size(19, 34);
            this.button_iso_minus.TabIndex = 5;
            this.button_iso_minus.Text = "-";
            this.button_iso_minus.UseVisualStyleBackColor = true;
            this.button_iso_minus.Click += new System.EventHandler(this.button_iso_minus_Click);
            // 
            // button_iso_plus
            // 
            this.button_iso_plus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_iso_plus.Location = new System.Drawing.Point(100, 160);
            this.button_iso_plus.Name = "button_iso_plus";
            this.button_iso_plus.Size = new System.Drawing.Size(19, 34);
            this.button_iso_plus.TabIndex = 6;
            this.button_iso_plus.Text = "+";
            this.button_iso_plus.UseVisualStyleBackColor = true;
            this.button_iso_plus.Click += new System.EventHandler(this.button_iso_plus_Click);
            // 
            // label_aperture
            // 
            this.label_aperture.AutoSize = true;
            this.label_aperture.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_aperture.Location = new System.Drawing.Point(3, 200);
            this.label_aperture.Name = "label_aperture";
            this.label_aperture.Size = new System.Drawing.Size(72, 40);
            this.label_aperture.TabIndex = 7;
            this.label_aperture.Text = "Aperture";
            this.label_aperture.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // button_aperture_minus
            // 
            this.button_aperture_minus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_aperture_minus.Location = new System.Drawing.Point(78, 200);
            this.button_aperture_minus.Name = "button_aperture_minus";
            this.button_aperture_minus.Size = new System.Drawing.Size(19, 34);
            this.button_aperture_minus.TabIndex = 8;
            this.button_aperture_minus.Text = "-";
            this.button_aperture_minus.UseVisualStyleBackColor = true;
            this.button_aperture_minus.Click += new System.EventHandler(this.button_aperture_minus_Click);
            // 
            // button_aperture_plus
            // 
            this.button_aperture_plus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_aperture_plus.Location = new System.Drawing.Point(100, 200);
            this.button_aperture_plus.Name = "button_aperture_plus";
            this.button_aperture_plus.Size = new System.Drawing.Size(19, 34);
            this.button_aperture_plus.TabIndex = 9;
            this.button_aperture_plus.Text = "+";
            this.button_aperture_plus.UseVisualStyleBackColor = true;
            this.button_aperture_plus.Click += new System.EventHandler(this.button_aperture_plus_Click);
            // 
            // label_shutter
            // 
            this.label_shutter.AutoSize = true;
            this.label_shutter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_shutter.Location = new System.Drawing.Point(3, 240);
            this.label_shutter.Name = "label_shutter";
            this.label_shutter.Size = new System.Drawing.Size(72, 40);
            this.label_shutter.TabIndex = 10;
            this.label_shutter.Text = "Shutter";
            this.label_shutter.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // button_shutter_minus
            // 
            this.button_shutter_minus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_shutter_minus.Location = new System.Drawing.Point(78, 240);
            this.button_shutter_minus.Name = "button_shutter_minus";
            this.button_shutter_minus.Size = new System.Drawing.Size(19, 34);
            this.button_shutter_minus.TabIndex = 11;
            this.button_shutter_minus.Text = "-";
            this.button_shutter_minus.UseVisualStyleBackColor = true;
            this.button_shutter_minus.Click += new System.EventHandler(this.button_shutter_minus_Click);
            // 
            // button_shutter_plus
            // 
            this.button_shutter_plus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_shutter_plus.Location = new System.Drawing.Point(100, 240);
            this.button_shutter_plus.Name = "button_shutter_plus";
            this.button_shutter_plus.Size = new System.Drawing.Size(19, 34);
            this.button_shutter_plus.TabIndex = 12;
            this.button_shutter_plus.Text = "+";
            this.button_shutter_plus.UseVisualStyleBackColor = true;
            this.button_shutter_plus.Click += new System.EventHandler(this.button_shutter_plus_Click);
            // 
            // label_iso_value
            // 
            this.label_iso_value.AutoSize = true;
            this.label_iso_value.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_iso_value.Location = new System.Drawing.Point(3, 160);
            this.label_iso_value.Name = "label_iso_value";
            this.label_iso_value.Size = new System.Drawing.Size(72, 30);
            this.label_iso_value.TabIndex = 13;
            this.label_iso_value.Text = "---";
            this.label_iso_value.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.label_iso_value.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold);
            // 
            // label_aperture_value
            // 
            this.label_aperture_value.AutoSize = true;
            this.label_aperture_value.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_aperture_value.Location = new System.Drawing.Point(3, 200);
            this.label_aperture_value.Name = "label_aperture_value";
            this.label_aperture_value.Size = new System.Drawing.Size(72, 30);
            this.label_aperture_value.TabIndex = 14;
            this.label_aperture_value.Text = "---";
            this.label_aperture_value.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.label_aperture_value.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold);
            // 
            // label_shutter_value
            // 
            this.label_shutter_value.AutoSize = true;
            this.label_shutter_value.Dock = System.Windows.Forms.DockStyle.Fill;
            this.label_shutter_value.Location = new System.Drawing.Point(3, 240);
            this.label_shutter_value.Name = "label_shutter_value";
            this.label_shutter_value.Size = new System.Drawing.Size(72, 30);
            this.label_shutter_value.TabIndex = 15;
            this.label_shutter_value.Text = "---";
            this.label_shutter_value.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.label_shutter_value.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold);
            // 
            // button_exposurePreview
            // 
            this.tableLayoutPanel2.Controls.Add(this.button_exposurePreview, 0, 13);
            this.tableLayoutPanel2.SetColumnSpan(this.button_exposurePreview, 3);
            this.button_exposurePreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_exposurePreview.Location = new System.Drawing.Point(3, 313);
            this.button_exposurePreview.Name = "button_exposurePreview";
            this.button_exposurePreview.Size = new System.Drawing.Size(138, 24);
            this.button_exposurePreview.TabIndex = 16;
            this.button_exposurePreview.Text = "Exposure Preview: --";
            this.button_exposurePreview.UseVisualStyleBackColor = true;
            this.button_exposurePreview.Click += new System.EventHandler(this.button_exposurePreview_Click);
            // 
            // button_meteringMode
            // 
            this.button_meteringMode.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button_meteringMode.Location = new System.Drawing.Point(3, 403);
            this.button_meteringMode.Name = "button_meteringMode";
            this.button_meteringMode.Size = new System.Drawing.Size(138, 24);
            this.button_meteringMode.TabIndex = 17;
            this.button_meteringMode.Text = "Metering: Matrix";
            this.button_meteringMode.UseVisualStyleBackColor = true;
            this.button_meteringMode.Click += new System.EventHandler(this.button_meteringMode_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 600); // Adjusted window size
            this.Controls.Add(this.tableLayoutPanel1);
            this.Name = "Form1";
            this.Text = "SKD750 Control";
            this.tableLayoutPanel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.tableLayoutPanel2.ResumeLayout(false);
            this.tableLayoutPanel2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.PictureBox pictureBox;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.Label label_name;
        private System.Windows.Forms.Button button_capture;
        private System.Windows.Forms.Button button_toggleliveview;
        private System.Windows.Forms.Button button_autofocus;
        private System.Windows.Forms.Label label_iso;
        private System.Windows.Forms.Button button_iso_minus;
        private System.Windows.Forms.Button button_iso_plus;
        private System.Windows.Forms.Label label_aperture;
        private System.Windows.Forms.Button button_aperture_minus;
        private System.Windows.Forms.Button button_aperture_plus;
        private System.Windows.Forms.Label label_shutter;
        private System.Windows.Forms.Button button_shutter_minus;
        private System.Windows.Forms.Button button_shutter_plus;
        private System.Windows.Forms.Label label_iso_value;
        private System.Windows.Forms.Label label_aperture_value;
        private System.Windows.Forms.Label label_shutter_value;
        private System.Windows.Forms.Button button_exposurePreview;
        private System.Windows.Forms.Button button_meteringMode;
        private System.Windows.Forms.Label label_afMode;
        private System.Windows.Forms.ComboBox comboBox_afMode;
    }
}
