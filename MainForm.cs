using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Nikon;
using System.Threading;
using SKD750Control.Backends;

namespace SKD750Control
{
    public partial class MainForm : Form
    {
        private ICameraBackend backend;
        private System.Windows.Forms.Timer liveViewTimer; // Specify the Timer type
        private int? focusX; // last focus point in image coordinates
        private int? focusY; // last focus point in image coordinates
        // Target position in sensor coordinates (persistent across zoom levels)
        private int? targetSensorX = null;
        private int? targetSensorY = null;
        // Camera Live View zoom via MAID capability 0x0000823F (0-6)
        private int cameraLvZoomLevel = 0; // current camera LV zoom: 0=off, 1-6=zoom levels
        // Long press to recenter
        private System.Windows.Forms.Timer longPressTimer;
        private bool isLongPressActive = false;

        private System.Windows.Forms.Timer captureDelayTimer; // timer to delay capture after AF
        private string afDebug = ""; // overlay debug text for AF state
        private bool flipY = false; // optional Y-axis flip for AF coord mapping
        private double afScaleX = 1.0;
        private double afScaleY = 1.0;
        private int afOffsetX = 0;
        private int afOffsetY = 0;
        // Actual Live View dimensions (from JPEG header)
        private int actualLvWidth = 1920;  // default, updated from LV stream
        private int actualLvHeight = 1080; // default, updated from LV stream
        // Last AF position from camera (sensor coordinates)
        private int? lastCameraAfX = null;
        private int? lastCameraAfY = null;
        // Last requested AF position (sensor coordinates)
        private int? lastRequestedAfX = null;
        private int? lastRequestedAfY = null;
        // Capability support flags (discovered on device connect)
        // Metering mode for overlay display
        private string currentMeteringMode = "Matrix"; // Default to Matrix
        // Live meter reading (ExposureStatus) overlay
        private string currentExposureStatus = "--";
        private bool exposureStatusErrorLogged = false;
        // Debounce flags for buttons to prevent rapid-click issues
        private bool shutterButtonProcessing = false;

        public MainForm() : this(false, null, 0)
        {
        }

        public MainForm(bool useDdServer, string ddHost, int ddPort)
        {
            InitializeComponent();

    // Set fullscreen mode
    this.WindowState = FormWindowState.Maximized;
    this.FormBorderStyle = FormBorderStyle.None;

            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                AppLogger.SetLogFile(logPath);
                AppLogger.Info("App started");
            }
            catch { }

            // Disable buttons
            ToggleButtons(false);

            // Initialize live view timer
            liveViewTimer = new System.Windows.Forms.Timer(); // Specify the Timer type
            liveViewTimer.Tick += liveViewTimer_Tick;
            liveViewTimer.Interval = 1000 / 30;

            // Timer used to delay capture until after contrast AF
            captureDelayTimer = new System.Windows.Forms.Timer();
            captureDelayTimer.Interval = 1500; // ms delay after AF trigger - increased for reliable focus
            captureDelayTimer.Tick += captureDelayTimer_Tick;
            
            // Timer for long press to recenter target
            longPressTimer = new System.Windows.Forms.Timer();
            longPressTimer.Interval = 1000; // 1 second
            longPressTimer.Tick += longPressTimer_Tick;
            
            // Enable key handling (Z toggles zoom preview)
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;

            // Initialize the camera backend (chosen via the pre-startup connection dialog
            // in Program.Main) and connect directly here in the constructor, exactly like
            // the original direct-USB NikonManager did. The native Nikon MAID scheduler must
            // be constructed from a clean top-level call stack - not from inside a nested
            // modal dialog or a posted BeginInvoke callback - or it crashes natively.
            if (useDdServer)
            {
                backend = new DdServerNikonBackend { Host = ddHost, Port = ddPort };
            }
            else
            {
                backend = new UsbNikonBackend();
            }

            backend.DeviceConnected += Backend_DeviceConnected;
            backend.DeviceDisconnected += Backend_DeviceDisconnected;
            backend.ImageReady += Backend_ImageReady;
            backend.CaptureComplete += Backend_CaptureComplete;
            backend.CapabilityValueChanged += Backend_CapabilityValueChanged;
            try
            {
                backend.Connect();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to connect camera backend", ex);
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Hook paint for focus box overlay; single-click will set point & trigger AF
            pictureBox.Paint += pictureBox_Paint;
        }






        protected override void OnClosing(CancelEventArgs e)
        {
            // Disable live view (in case it's enabled)
            if (backend != null && backend.IsConnected)
            {
                backend.LiveViewEnabled = false;
            }

            // Shut down the camera backend
            backend?.Shutdown();
            base.OnClosing(e);
        }

        private void Backend_DeviceConnected(string deviceName)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => Backend_DeviceConnected(deviceName)));
                return;
            }

            // Set the device name
            label_name.Text = deviceName;

            // Enable buttons
            ToggleButtons(true);

            // Log Point-type capabilities for AF diagnostics
            try
            {
                var caps = backend.GetCapabilityInfo();
                AppLogger.Info($"Device connected: {caps.Length} capabilities total");
                foreach (var cap in caps)
                {
                    if (cap.Type == eNkMAIDCapType.kNkMAIDCapType_Point)
                    {
                        AppLogger.Info($"PointCap: ID={(uint)cap.Id:X} [{cap.Description}] CanGet={cap.CanGetValue} CanSet={cap.CanSetValue}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to enumerate Point capabilities", ex);
            }

            // Initial update of values
            UpdateCameraValues();
            UpdateExposurePreviewUI();
            UpdateAFModeUI();
            UpdateMeteringModeDisplay();

            // Probe for Live View zoom/magnification related capabilities and log them
            try { ProbeLvZoomCapabilities(); } catch { }

        }

        private void Backend_DeviceDisconnected()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)Backend_DeviceDisconnected);
                return;
            }

            // Stop live view timer
            liveViewTimer.Stop();

            // Clear device name
            label_name.Text = "No Camera";

            // Disable buttons
            ToggleButtons(false);

            // Clear live view picture
            pictureBox.Image = null;
        }

        private void Backend_ImageReady(byte[] buffer, string ext)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => Backend_ImageReady(buffer, ext)));
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = ext == "jpg" ?
                    "Jpeg Image (*.jpg)|*.jpg" :
                    "Nikon NEF (*.nef)|*.nef";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write))
                    {
                        stream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
        }

        private void Backend_CaptureComplete()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)Backend_CaptureComplete);
                return;
            }

            // Re-enable buttons when the capture completes
            ToggleButtons(true);
        }

        private void Backend_CapabilityValueChanged(eNkMAIDCapability capability)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => Backend_CapabilityValueChanged(capability)));
                return;
            }

            // Refresh displayed values when relevant capabilities change
            if (capability == eNkMAIDCapability.kNkMAIDCapability_Sensitivity ||
                capability == eNkMAIDCapability.kNkMAIDCapability_Aperture ||
                capability == eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed)
            {
                UpdateCameraValues();
            }
            if (capability == eNkMAIDCapability.kNkMAIDCapability_LiveViewExposurePreview)
            {
                UpdateExposurePreviewUI();
            }
            if (capability == eNkMAIDCapability.kNkMAIDCapability_MeteringMode)
            {
                UpdateMeteringModeDisplay();
            }
        }

        private void liveViewTimer_Tick(object sender, EventArgs e)
        {
            // Get live view image
            byte[] jpegBuffer = null;

            try
            {
                jpegBuffer = backend.GetLiveViewImage();
            }
            catch (NikonException ex)
            {
                liveViewTimer.Stop();
                MessageBox.Show($"Live view error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("Live view error", ex);
            }
            catch (Exception ex)
            {
                LogError("Live view error (backend)", ex);
            }

            // Set live view image on picture box with rotation if valid
            if (jpegBuffer != null && jpegBuffer.Length > 16)
            {
                try
                {
                       // Parse Live View header for AF position (DigiCamControl pattern)
                       // D750 is Type0015 (header=384 bytes). AF position at offsets 28,30 (16-bit int)
                       // Get the camera's inclination FIRST to determine rotation state
                       uint inclination = 0;
                       try { inclination = backend.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CameraInclination); } catch { }
                       bool isPortrait = (inclination == 1);

                       try
                       {
                           if (jpegBuffer.Length > 384)
                           {
                               int lvFocusX = BitConverter.ToInt16(jpegBuffer, 28);
                               int lvFocusY = BitConverter.ToInt16(jpegBuffer, 30);
                               int lvImageWidth = BitConverter.ToInt16(jpegBuffer, 12);
                               int lvImageHeight = BitConverter.ToInt16(jpegBuffer, 14);

                               // Store actual LV dimensions - swap if portrait to match rotated display
                               if (isPortrait)
                               {
                                   actualLvWidth = lvImageHeight;
                                   actualLvHeight = lvImageWidth;
                               }
                               else
                               {
                                   actualLvWidth = lvImageWidth;
                                   actualLvHeight = lvImageHeight;
                               }

                               // Store camera's current AF position (sensor coordinates)
                               lastCameraAfX = lvFocusX;
                               lastCameraAfY = lvFocusY;

                               // Log comparison if we recently requested an AF position
                               if (lastRequestedAfX.HasValue && lastRequestedAfY.HasValue)
                               {
                                   int deltaX = lvFocusX - lastRequestedAfX.Value;
                                   int deltaY = lvFocusY - lastRequestedAfY.Value;
                                   double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                                   AppLogger.Info($"AF Position: Requested=({lastRequestedAfX},{lastRequestedAfY}) Camera=({lvFocusX},{lvFocusY}) Delta=({deltaX},{deltaY}) Distance={distance:F1}px");
                               }

                               AppLogger.Info($"LV Header: ImageSize=({lvImageWidth},{lvImageHeight}) AFPos=({lvFocusX},{lvFocusY}) Portrait={isPortrait} StoredAs=({actualLvWidth},{actualLvHeight})");
                           }
                       }
                       catch (Exception headerEx)
                       {
                           AppLogger.Warn($"Failed to parse LV header: {headerEx.Message}");
                       }

                    using (MemoryStream stream = new MemoryStream(jpegBuffer))
                    {
                        Image liveViewImage = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

                        if (isPortrait)
                        {
                            liveViewImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                        }

                        pictureBox.Image = liveViewImage;

                        // Read ExposureStatus live (EV). Avoid log spam on repeated failures.
                        try
                        {
                            float ev = backend.GetFloat(eNkMAIDCapability.kNkMAIDCapability_ExposureStatus);
                            currentExposureStatus = ev.ToString("F2");
                            exposureStatusErrorLogged = false;
                        }
                        catch (Exception exStat)
                        {
                            currentExposureStatus = "--";
                            if (!exposureStatusErrorLogged)
                            {
                                AppLogger.Error($"ExposureStatus read failed: {exStat.GetType().Name}: {exStat.Message}", exStat);
                                exposureStatusErrorLogged = true;
                            }
                        }

                        // Log the display image size for clarity (distinct from LV header)
                        try
                        {
                            AppLogger.Info($"LiveView Display Image: size=({liveViewImage.Width},{liveViewImage.Height}) orientation={(inclination==1?"portrait":"landscape")}");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    // Ignore bad frames during mode changes; log for diagnostics
                    LogError("Live view frame decode error", ex);
                }
            }
        }

        // Cycle camera Live View zoom with Z key (0->1->2->3->4->5->6->0...)
        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Z)
            {
                if (device == null || !device.LiveViewEnabled)
                {
                    e.Handled = true;
                    return;
                }

                // Cycle through camera LV zoom levels 0-6
                cameraLvZoomLevel = (cameraLvZoomLevel + 1) % 7;

                try
                {
                    // CapID 0x0000823F = Live View Image Zoom Rate (Enum: 0-6)
                    var cap = (eNkMAIDCapability)0x0000823F;
                    var zoomEnum = device.GetEnum(cap);
                    if (zoomEnum != null && cameraLvZoomLevel < zoomEnum.Length)
                    {
                        zoomEnum.Index = cameraLvZoomLevel;
                        device.SetEnum(cap, zoomEnum);
                        AppLogger.Info($"Camera LV Zoom set to level {cameraLvZoomLevel}");
                        // Update UI to show zoom state (optional status in title or label)
                        this.Text = cameraLvZoomLevel == 0 ? "SKD750 Control" : $"SKD750 Control - LV Zoom {cameraLvZoomLevel}";
                    }
                    else
                    {
                        AppLogger.Warn($"LV Zoom level {cameraLvZoomLevel} out of range or cap unavailable");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to set camera LV zoom to {cameraLvZoomLevel}", ex);
                }

                e.Handled = true;
                return;
            }

            // F12: dump all device capabilities to a file for quick reference
            if (e.KeyCode == Keys.F12)
            {
                try
                {
                    DumpCapabilitiesToFile();
                    AppLogger.Info("Capability dump complete (F12)");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to dump capabilities", ex);
                }
                e.Handled = true;
                return;
            }

            // M: cycle through metering modes (0=Matrix, 1=Center-weighted, 2=Spot)
            if (e.KeyCode == Keys.M)
            {
                if (device == null)
                {
                    e.Handled = true;
                    return;
                }

                try
                {
                    uint meteringMode = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode);
                    // D750 supports: 0=Matrix, 1=Center-weighted, 2=Spot (mode 3 is invalid)
                    uint newMode = (meteringMode + 1) % 3;
                    device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode, newMode);
                    
                    // Update display
                    UpdateMeteringModeDisplay();
                    AppLogger.Info($"Metering mode changed to {newMode}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to change metering mode", ex);
                }
                e.Handled = true;
                return;
            }

            // Ctrl+E: probe ExposureStatus (meter EV) and report
            if (e.Control && e.KeyCode == Keys.E)
            {
                if (device == null)
                {
                    MessageBox.Show("No camera connected");
                    e.Handled = true;
                    return;
                }
                try
                {
                    float ev = (float)device.GetFloat(eNkMAIDCapability.kNkMAIDCapability_ExposureStatus);
                    AppLogger.Info($"ExposureStatus (EV): {ev}");
                    MessageBox.Show($"ExposureStatus (EV): {ev:F2}", "ExposureStatus");
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"ExposureStatus read failed: {ex.GetType().Name}: {ex.Message}", ex);
                    MessageBox.Show($"ExposureStatus not available: {ex.Message}", "ExposureStatus Error");
                }
                e.Handled = true;
                return;
            }
        }

        // Enumerate all capabilities reported by the device and write to a file
        private void DumpCapabilitiesToFile()
        {
            if (device == null)
            {
                MessageBox.Show("No camera connected", "Capability Dump", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var caps = device.GetCapabilityInfo();
            var sb = new StringBuilder();
            sb.AppendLine($"Device: {device.Name}");
            sb.AppendLine($"Total capabilities: {caps.Length}");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("ID(hex)\tType\tCanGet\tCanSet\tDescription");

            foreach (var cap in caps)
            {
                string type = cap.ulType.ToString();
                string canGet = cap.CanGet().ToString();
                string canSet = cap.CanSet().ToString();
                string desc = cap.GetDescription();
                sb.AppendLine($"0x{cap.ulID:X}\t{type}\t{canGet}\t{canSet}\t{desc}");
            }

            var outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capability_dumps");
            Directory.CreateDirectory(outDir);
            var fileName = $"caps_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var outPath = Path.Combine(outDir, fileName);
            File.WriteAllText(outPath, sb.ToString());

            try { AppLogger.Info($"Capabilities written to {outPath}"); } catch { }
            MessageBox.Show($"Capabilities saved to:\n{outPath}", "Capability Dump", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void device_ImageReady(NikonDevice sender, NikonImage image)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = (image.Type == NikonImageType.Jpeg) ?
                    "Jpeg Image (*.jpg)|*.jpg" :
                    "Nikon NEF (*.nef)|*.nef";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write))
                    {
                        stream.Write(image.Buffer, 0, image.Buffer.Length);
                    }
                }
            }
        }

        private void device_CaptureComplete(NikonDevice sender, int data)
        {
            // Re-enable buttons when the capture completes
            ToggleButtons(true);
        }

        private void device_CapabilityValueChanged(NikonDevice sender, eNkMAIDCapability capability)
        {
            // Refresh displayed values when relevant capabilities change
            if (capability == eNkMAIDCapability.kNkMAIDCapability_Sensitivity ||
                capability == eNkMAIDCapability.kNkMAIDCapability_Aperture ||
                capability == eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed)
            {
                UpdateCameraValues();
            }
            if (capability == eNkMAIDCapability.kNkMAIDCapability_LiveViewExposurePreview)
            {
                UpdateExposurePreviewUI();
            }
            if (capability == eNkMAIDCapability.kNkMAIDCapability_MeteringMode)
            {
                UpdateMeteringModeDisplay();
            }
        }

        private void UpdateMeteringModeDisplay()
        {
            if (device == null) return;
            try
            {
                uint modeValue = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode);
                // Map mode value to full name: 0=Matrix, 1=Center-weighted, 2=Spot
                // D750 doesn't support mode 3 (Highlight)
                switch (modeValue)
                {
                    case 0: currentMeteringMode = "Matrix"; break;
                    case 1: currentMeteringMode = "Center-weighted"; break;
                    case 2: currentMeteringMode = "Spot"; break;
                    default: currentMeteringMode = "Matrix"; break; // Default to Matrix for unknown values
                }
                // Update button if it exists
                if (button_meteringMode != null)
                    button_meteringMode.Text = $"Metering: {currentMeteringMode}";
            }
            catch { }
        }

        private void ToggleButtons(bool enabled)
        {
            button_capture.Enabled = enabled;
            button_toggleliveview.Enabled = enabled;
            button_autofocus.Enabled = enabled;
        }

        private void button_capture_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            
            try
            {
                ToggleButtons(false);
                device.Capture();
            }
            catch (NikonException ex)
            {
                MessageBox.Show($"Capture error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("Capture error", ex);
                ToggleButtons(true);
            }
        }

        private void button_toggleliveview_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            
            if (device.LiveViewEnabled)
            {
                device.LiveViewEnabled = false;
                liveViewTimer.Stop();
                pictureBox.Image = null;
            }
            else
            {
                device.LiveViewEnabled = true;
                liveViewTimer.Start();
                // Initialize debug overlay so something is visible immediately
                afDebug = "AFDBG: init";
                
                // Set default focus point to center of sensor
                targetSensorX = 6016 / 2; // D750 sensor width
                targetSensorY = 4016 / 2; // D750 sensor height

                UpdateExposurePreviewUI();
            }
        }

        private void button_autofocus_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                if (device.LiveViewEnabled)
                {
                    device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_ContrastAF, 0);
                }
                else
                {
                    device.Start(eNkMAIDCapability.kNkMAIDCapability_AutoFocus);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Autofocus error: {ex.Message}");
                LogError("Autofocus error", ex);
            }
        }


        

        private void captureDelayTimer_Tick(object sender, EventArgs e)
        {
            captureDelayTimer.Stop();
            PerformImmediateCapture();
        }

        private void PerformImmediateCapture()
        {
            ToggleButtons(false);
            try
            {
                device.Capture();
            }
            catch (NikonException ex)
            {
                MessageBox.Show($"Capture error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("Capture error", ex);
                ToggleButtons(true);
            }
            pictureBox.Image = null;
        }

        private void comboBox_afMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (device == null || comboBox_afMode.SelectedIndex < 0) return;
            try
            {
                var cap = eNkMAIDCapability.kNkMAIDCapability_AFModeAtLiveView;
                var afEnum = device.GetEnum(cap);
                if (afEnum != null)
                {
                    // 0 = AF-S, 1 = AF-C
                    afEnum.Index = comboBox_afMode.SelectedIndex;
                    device.SetEnum(cap, afEnum);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set AF mode: {ex.Message}");
                LogError("Failed to set AF mode", ex);
            }
        }

        private void UpdateAFModeUI()
        {
            if (device == null) return;
            try
            {
                var cap = eNkMAIDCapability.kNkMAIDCapability_AFModeAtLiveView;
                if (comboBox_afMode != null)
                {
                    try
                    {
                        var afEnum = device.GetEnum(cap);
                        if (afEnum != null && afEnum.Index < 2)
                        {
                            comboBox_afMode.SelectedIndex = afEnum.Index;
                        }
                    }
                    catch
                    {
                        comboBox_afMode.SelectedIndex = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error updating AF mode UI", ex);
            }
        }

            

        private void longPressTimer_Tick(object sender, EventArgs e)
        {
            longPressTimer.Stop();
            isLongPressActive = true;
            
            // Recenter target to sensor center
            if (cameraLvZoomLevel == 0)
            {
                targetSensorX = 6016 / 2;
                targetSensorY = 4016 / 2;
                pictureBox.Invalidate();
                AppLogger.Info("Target recentered to sensor center via long press");
            }
        }

        private void pictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            // Start long press timer on mouse down (only at baseline)
            if (cameraLvZoomLevel == 0 && device != null && device.LiveViewEnabled)
            {
                isLongPressActive = false;
                longPressTimer.Start();
            }
        }

        private void pictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            // Stop long press timer on mouse up
            longPressTimer.Stop();
        }

        private void pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            // If long press was triggered, don't process click
            if (isLongPressActive)
            {
                isLongPressActive = false;
                return;
            }
            
            // Guard: require live view header dimensions before click-to-focus
            if (actualLvWidth <= 0 || actualLvHeight <= 0)
            {
                AppLogger.Warn($"Click ignored: LiveView dimensions not ready actualLv=({actualLvWidth}x{actualLvHeight})");
                return;
            }
            if (device == null || !device.LiveViewEnabled || pictureBox.Image == null) return;

            // When zoomed (not at baseline), only trigger autofocus without changing focus point
            if (cameraLvZoomLevel > 0)
            {
                // Just trigger AF using existing stored sensor coordinates
                if (targetSensorX.HasValue && targetSensorY.HasValue)
                {
                    try
                    {
                        device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_ContrastAF, 0);
                        AppLogger.Info($"ContrastAF trigger (zoomed, using stored point sensor=({targetSensorX.Value},{targetSensorY.Value}))");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("ContrastAF trigger failed (zoomed)", ex);
                    }
                }
                else
                {
                    AppLogger.Warn("Click ignored while zoomed: no stored focus point");
                }
                return;
            }

            // At baseline: set new focus point and trigger autofocus
            try
            {
                // Convert click coordinates to image coordinates
                int imgWidth = pictureBox.Image.Width;
                int imgHeight = pictureBox.Image.Height;
                int boxWidth = pictureBox.Width;
                int boxHeight = pictureBox.Height;
                
                float imgAspect = (float)imgWidth / imgHeight;
                float boxAspect = (float)boxWidth / boxHeight;
                
                int displayWidth, displayHeight, offsetX, offsetY;
                
                if (imgAspect > boxAspect)
                {
                    // Image is wider - letterbox top/bottom
                    displayWidth = boxWidth;
                    displayHeight = (int)(boxWidth / imgAspect);
                    offsetX = 0;
                    offsetY = (boxHeight - displayHeight) / 2;
                }
                else
                {
                    // Image is taller - pillarbox left/right
                    displayWidth = (int)(boxHeight * imgAspect);
                    displayHeight = boxHeight;
                    offsetX = (boxWidth - displayWidth) / 2;
                    offsetY = 0;
                }
                
                // Check if click is within the displayed image
                if (e.X < offsetX || e.X > offsetX + displayWidth ||
                    e.Y < offsetY || e.Y > offsetY + displayHeight)
                {
                    return; // Click was in letterbox/pillarbox area
                }
                
                // Convert to image coordinates (0-imgWidth, 0-imgHeight)
                int imgX = (int)((e.X - offsetX) * imgWidth / displayWidth);
                int imgY = (int)((e.Y - offsetY) * imgHeight / displayHeight);

                // Prefer sending AF area via MTP command 0x9205; fallback to ContrastAFArea
                // Optional Y flip for landscape calibration
                // Compensate for cursor hotspot (tip is below and right of center)
                const int cursorOffsetX = 2;  // pixels to shift right
                const int cursorOffsetY = 1;  // pixels to shift down
                int adjustedX = imgX + cursorOffsetX;
                int adjustedY = imgY + cursorOffsetY;
                
                // Apply adjustable scale/offset mapping
                int tx = (int)Math.Round(adjustedX * afScaleX) + afOffsetX;
                int ty = (int)Math.Round(adjustedY * afScaleY) + afOffsetY;
                // Clamp to image bounds
                tx = Math.Max(0, Math.Min(imgWidth - 1, tx));
                ty = Math.Max(0, Math.Min(imgHeight - 1, ty));
                try
                {
                    var h = pictureBox.Image != null ? pictureBox.Image.Height : pictureBox.Height;
                    if (flipY && h > 0)
                    {
                        ty = Math.Max(0, h - ty - 1);
                    }
                }
                catch { }

                // Log focal length and crop mode for diagnostics
                string focalLengthInfo = "unknown";
                try 
                { 
                    var focalLen = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_FocalLength);
                    focalLengthInfo = $"{focalLen}mm";
                }
                catch { }
                
                // Check camera orientation (portrait mode needs coordinate rotation)
                uint inclination = 0;
                try { inclination = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CameraInclination); } catch { }
                bool isPortrait = (inclination == 1);
                
                AppLogger.Info($"ClickToFocus raw=({imgX},{imgY}) mapped=({tx},{ty}) flipY={flipY} scale=({afScaleX:F2},{afScaleY:F2}) offset=({afOffsetX},{afOffsetY}) focal={focalLengthInfo} portrait={isPortrait}");
                
                // ContrastAFArea expects sensor-based coordinates
                // D750 sensor: 6016×4016 (24MP FX format)
                // Live View is downsampled, so scale up to sensor resolution
                const int sensorWidth = 6016;
                const int sensorHeight = 4016;
                
                int sensorX, sensorY;
                
                // Use preview image dimensions for mapping (constant regardless of zoom)
                // In portrait, imgWidth/Height are post-rotation, so swap back to get original frame size
                int originalLvWidth, originalLvHeight;
                if (isPortrait)
                {
                    originalLvWidth = imgHeight;
                    originalLvHeight = imgWidth;
                }
                else
                {
                    originalLvWidth = imgWidth;
                    originalLvHeight = imgHeight;
                }
                
                AppLogger.Info($"Dimensions: image=({imgWidth}x{imgHeight}) lv=({actualLvWidth}x{actualLvHeight}) mappingBasis=({originalLvWidth}x{originalLvHeight}) focal={focalLengthInfo}");
                
                if (isPortrait)
                {
                    // Portrait mode: Camera rotated 90° CCW physically → software rotates image 270° CW to display upright
                    // Click coordinates (tx,ty) are in rotated display space: imgWidth × imgHeight (narrow × tall)
                    // Reverse 270° CW rotation using display dimensions:
                    // (tx, ty) in rotated space → (imgHeight - 1 - ty, tx) in original space
                    int origX = imgHeight - 1 - ty;
                    int origY = tx;
                    
                    // Now scale to sensor using original (pre-rotation) dimensions
                    sensorX = (int)((origX / (float)originalLvWidth) * sensorWidth);
                    sensorY = (int)((origY / (float)originalLvHeight) * sensorHeight);
                    
                    AppLogger.Info($"Portrait unwrap: click=({tx},{ty}) display=({imgWidth}x{imgHeight}) origClick=({origX},{origY}) mappingBasis=({originalLvWidth}x{originalLvHeight}) -> sensor=({sensorX},{sensorY})");
                }
                else
                {
                    // Landscape mode: direct mapping
                    sensorX = (int)((tx / (float)originalLvWidth) * sensorWidth);
                    sensorY = (int)((ty / (float)originalLvHeight) * sensorHeight);
                }
                
                // Clamp to valid sensor range
                sensorX = Math.Max(0, Math.Min(sensorWidth - 1, sensorX));
                sensorY = Math.Max(0, Math.Min(sensorHeight - 1, sensorY));
                
                AppLogger.Info($"Sensor coordinates: ({sensorX},{sensorY}) from LiveView ({originalLvWidth}x{originalLvHeight}) -> Sensor ({sensorWidth}x{sensorHeight}) portrait={isPortrait}");
                
                // Use MAID ContrastAFArea - set AF point
                try
                {
                    var focusPoint = new NkMAIDPoint { x = sensorX, y = sensorY };
                    device.SetPoint(eNkMAIDCapability.kNkMAIDCapability_ContrastAFArea, focusPoint);
                    
                    // Store requested position for later comparison
                    lastRequestedAfX = sensorX;
                    lastRequestedAfY = sensorY;
                    
                    AppLogger.Info($"ContrastAFArea SetPoint: sensor=({sensorX},{sensorY}) liveView=({tx},{ty})");
                    afDebug = $"AF: ({sensorX},{sensorY})";
                }
                catch (Exception afSetEx)
                {
                    AppLogger.Error($"ContrastAFArea SetPoint failed: ({tx},{ty})", afSetEx);
                    afDebug = $"AF Fail: {afSetEx.Message}";
                    return; // Don't trigger AF if set failed
                }

                // Update overlay with current state
                try
                {
                    afDebug = $"AFPoint=({tx},{ty})";
                }
                catch { }

                // store sensor coordinates (persistent across zoom) and image coordinates (for display)
                targetSensorX = sensorX;
                targetSensorY = sensorY;
                focusX = tx;
                focusY = ty;
                pictureBox.Invalidate();
                
                // Trigger contrast AF immediately on single click
                try
                {
                    device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_ContrastAF, 0);
                    AppLogger.Info("ContrastAF trigger sent");
                }
                catch (NikonException nEx)
                {
                    // If device busy, wait briefly and retry once
                    if (nEx.ErrorCode == eNkMAIDResult.kNkMAIDResult_DeviceBusy)
                    {
                        try { Thread.Sleep(150); device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_ContrastAF, 0); }
                        catch { LogError("Contrast AF trigger error (retry)", nEx); }
                        AppLogger.Warn("ContrastAF trigger busy; retried");
                    }
                    else
                    {
                        LogError("Contrast AF trigger error", nEx);
                        AppLogger.Error("ContrastAF trigger error", nEx);
                    }
                }
                catch (Exception afEx)
                {
                    LogError("Contrast AF trigger error", afEx);
                    AppLogger.Error("ContrastAF trigger error", afEx);
                }
            }
            catch (Exception ex)
            {
                LogError("Error setting focus point", ex);
            }
        }

        // Create/update the zoom preview image from the current PictureBox image


        private void RunAfCalibration()
        {
            if (device == null || pictureBox.Image == null)
            {
                MessageBox.Show("Enable Live View before calibrating AF.");
                return;
            }

            AppLogger.Info("=== AF Calibration Started ===");
            try
            {
                int imgWidth = pictureBox.Image.Width;
                int imgHeight = pictureBox.Image.Height;
                int[] testX = { 50, imgWidth / 2, imgWidth - 50 };
                int[] testY = { 50, imgHeight / 2, imgHeight - 50 };

                var caps = device.GetCapabilityInfo();
                uint contrastCapId = (uint)eNkMAIDCapability.kNkMAIDCapability_ContrastAFArea;
                var contrastCap = caps.FirstOrDefault(c => (uint)c.ulID == contrastCapId);
                bool canReadBack = contrastCap.ulID != 0 && contrastCap.CanGet();

                AppLogger.Info($"Image size=({imgWidth},{imgHeight}) CanReadBack={canReadBack}");

                foreach (var x in testX)
                {
                    foreach (var y in testY)
                    {
                        var pt = new NkMAIDPoint { x = x, y = y };
                        device.SetPoint(eNkMAIDCapability.kNkMAIDCapability_ContrastAFArea, pt);
                        Thread.Sleep(120);

                        if (canReadBack)
                        {
                            try
                            {
                                var rb = device.GetPoint(eNkMAIDCapability.kNkMAIDCapability_ContrastAFArea);
                                AppLogger.Info($"CalibTest rawSet=({x},{y}) readBack=({rb.x},{rb.y})");
                            }
                            catch { }
                        }
                        else
                        {
                            AppLogger.Info($"CalibTest rawSet=({x},{y}) [no readback]");
                        }
                    }
                }

                AppLogger.Info("=== AF Calibration Complete ===");
                MessageBox.Show("AF calibration complete. Check app.log for results.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("AF calibration error", ex);
                MessageBox.Show($"Calibration error: {ex.Message}");
            }
        }

        private void pictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (pictureBox.Image == null) return;

            int imgWidth = pictureBox.Image.Width;
            int imgHeight = pictureBox.Image.Height;
            int boxWidth = pictureBox.Width;
            int boxHeight = pictureBox.Height;

            float imgAspect = (float)imgWidth / imgHeight;
            float boxAspect = (float)boxWidth / boxHeight;

            int displayWidth, displayHeight, offsetX, offsetY;
            if (imgAspect > boxAspect)
            {
                displayWidth = boxWidth;
                displayHeight = (int)(boxWidth / imgAspect);
                offsetX = 0;
                offsetY = (boxHeight - displayHeight) / 2;
            }
            else
            {
                displayWidth = (int)(boxHeight * imgAspect);
                displayHeight = boxHeight;
                offsetX = (boxWidth - displayWidth) / 2;
                offsetY = 0;
            }

            // Only draw target box when at baseline zoom (level 0)
            // When zoomed, we can't accurately position the box, so hide it
            if (cameraLvZoomLevel == 0 && targetSensorX.HasValue && targetSensorY.HasValue)
            {
                // Check if camera is in portrait mode
                uint inclination = 0;
                bool isPortrait = false;
                try 
                { 
                    if (device != null)
                    {
                        inclination = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_CameraInclination);
                        isPortrait = (inclination == 1);
                    }
                } 
                catch { }
                
                // Map target position from sensor coordinates to image coordinates
                const int fullSensorWidth = 6016;
                const int fullSensorHeight = 4016;
                
                int fx, fy;
                
                if (isPortrait)
                {
                    // In portrait mode, the displayed image is rotated 270° CW from sensor orientation
                    // Sensor coords (targetSensorX, targetSensorY) need to be transformed to rotated display
                    // Reverse of portrait unwrap: sensor (x,y) -> display (y, imgHeight - 1 - x)
                    // But imgWidth/Height here are already post-rotation (424x640), so we need original dimensions
                    int originalLvWidth = imgHeight;  // swap because image is rotated
                    int originalLvHeight = imgWidth;
                    
                    // Map sensor to original (pre-rotation) image space
                    int origX = (int)((float)targetSensorX.Value * originalLvWidth / fullSensorWidth);
                    int origY = (int)((float)targetSensorY.Value * originalLvHeight / fullSensorHeight);
                    
                    // Apply 270° CW rotation: original (origX, origY) -> rotated (origY, imgHeight-1-origX)
                    fx = origY;
                    fy = imgHeight - 1 - origX;
                }
                else
                {
                    // Landscape mode: direct mapping
                    fx = (int)((float)targetSensorX.Value * imgWidth / fullSensorWidth);
                    fy = (int)((float)targetSensorY.Value * imgHeight / fullSensorHeight);
                }
                
                fx = Math.Max(0, Math.Min(imgWidth - 1, fx));
                fy = Math.Max(0, Math.Min(imgHeight - 1, fy));

                // transform image coords to display coords
                float scaleX = (float)displayWidth / imgWidth;
                float scaleY = (float)displayHeight / imgHeight;
                int dispX = offsetX + (int)(fx * scaleX);
                int dispY = offsetY + (int)(fy * scaleY);

                // draw a 32x32 rectangle centered at focus point
                int rectSize = 32;
                int rectX = dispX - rectSize / 2;
                int rectY = dispY - rectSize / 2;

                using (Pen p = new Pen(Color.Lime, 2))
                {
                    e.Graphics.DrawRectangle(p, rectX, rectY, rectSize, rectSize);
                }
            }

            // Draw AF debug overlay (top-left) – always show something
            using (var f = new Font("Segoe UI", 8f))
            using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            using (var fg = new SolidBrush(Color.White))
            {
                var baseText = string.IsNullOrWhiteSpace(afDebug) ? $"AFDBG: fx={(focusX??imgWidth/2)}, fy={(focusY??imgHeight/2)}" : afDebug;
                var extra = $" | image={imgWidth}x{imgHeight} lv={actualLvWidth}x{actualLvHeight}";
                var text = baseText + extra;
                var size = e.Graphics.MeasureString(text, f);
                var rect = new RectangleF(5, 5, size.Width + 6, size.Height + 4);
                e.Graphics.FillRectangle(bg, rect);
                e.Graphics.DrawString(text, f, fg, new PointF(8, 7));
            }

            // Draw camera settings overlay (bottom) - Aperture, ISO, Shutter Speed, Metering Mode
            if (device != null && device.LiveViewEnabled)
            {
                string isoText = label_iso_value.Text;
                string apertureText = label_aperture_value.Text;
                string shutterText = label_shutter_value.Text;
                
                // Settings text (without EV)
                string settingsText = $"{isoText}  |  f/{apertureText}  |  {shutterText}  |  METERING: {currentMeteringMode}";
                
                using (var f = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (var smallFont = new Font("Segoe UI", 9f))
                using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                using (var fg = new SolidBrush(Color.White))
                using (var meterBg = new SolidBrush(Color.FromArgb(200, 40, 40, 40)))
                using (var meterFill = new SolidBrush(Color.FromArgb(220, 0, 200, 0)))
                using (var centerLine = new Pen(Color.Yellow, 2))
                using (var tickPen = new Pen(Color.Gray, 1))
                {
                    var size = e.Graphics.MeasureString(settingsText, f);
                    float xPos = (boxWidth - size.Width) / 2;
                    float yPos = boxHeight - size.Height - 60;
                    var rect = new RectangleF(xPos - 10, yPos - 5, size.Width + 20, size.Height + 10);
                    e.Graphics.FillRectangle(bg, rect);
                    e.Graphics.DrawString(settingsText, f, fg, new PointF(xPos, yPos));

                    // Draw EV meter below settings text
                    float meterY = yPos + size.Height + 15;
                    float meterWidth = 400;
                    float meterHeight = 20;
                    float meterX = (boxWidth - meterWidth) / 2;
                    
                    // Meter background
                    var meterRect = new RectangleF(meterX, meterY, meterWidth, meterHeight);
                    e.Graphics.FillRectangle(meterBg, meterRect);
                    e.Graphics.DrawRectangle(Pens.Gray, meterX, meterY, meterWidth, meterHeight);
                    
                    // Draw tick marks for -3, -2, -1, 0, +1, +2, +3
                    for (int i = -3; i <= 3; i++)
                    {
                        float tickX = meterX + (meterWidth / 2) + (i * (meterWidth / 6));
                        e.Graphics.DrawLine(tickPen, tickX, meterY, tickX, meterY + meterHeight);
                        string label = i > 0 ? $"+{i}" : i.ToString();
                        var labelSize = e.Graphics.MeasureString(label, smallFont);
                        e.Graphics.DrawString(label, smallFont, fg, new PointF(tickX - labelSize.Width / 2, meterY + meterHeight + 2));
                    }
                    
                    // Center line (0 EV)
                    float centerX = meterX + (meterWidth / 2);
                    e.Graphics.DrawLine(centerLine, centerX, meterY, centerX, meterY + meterHeight);
                    
                    // Parse and draw current EV indicator
                    if (currentExposureStatus != "--" && float.TryParse(currentExposureStatus, out float ev))
                    {
                        // Clamp EV to -3 to +3 range for display
                        float clampedEv = Math.Max(-3f, Math.Min(3f, ev));
                        float evX = meterX + (meterWidth / 2) + (clampedEv * (meterWidth / 6));
                        
                        // Draw indicator bar from center to current EV
                        if (clampedEv >= 0)
                        {
                            float barX = centerX;
                            float barWidth = evX - centerX;
                            e.Graphics.FillRectangle(meterFill, barX, meterY + 2, barWidth, meterHeight - 4);
                        }
                        else
                        {
                            float barWidth = centerX - evX;
                            e.Graphics.FillRectangle(meterFill, evX, meterY + 2, barWidth, meterHeight - 4);
                        }
                        
                        // Draw EV value text above meter
                        string evLabel = $"EV {ev:F2}";
                        var evSize = e.Graphics.MeasureString(evLabel, f);
                        e.Graphics.DrawString(evLabel, f, fg, new PointF(meterX + (meterWidth - evSize.Width) / 2, meterY - evSize.Height - 2));
                    }
                    else
                    {
                        // No valid EV - show "--"
                        string evLabel = "EV --";
                        var evSize = e.Graphics.MeasureString(evLabel, f);
                        e.Graphics.DrawString(evLabel, f, fg, new PointF(meterX + (meterWidth - evSize.Width) / 2, meterY - evSize.Height - 2));
                    }
                }
            }
        }

        private void button_iso_minus_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity);
                if (en == null) { MessageBox.Show("ISO not available"); return; }
                if (en.Index > 0)
                {
                    en.Index = en.Index - 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity, en);
                    UpdateCameraValues();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adjusting ISO: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("ISO adjustment error", ex);
            }
        }

        private void button_iso_plus_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity);
                if (en == null) { MessageBox.Show("ISO not available"); return; }
                if (en.Index < en.Length - 1)
                {
                    en.Index = en.Index + 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity, en);
                    // verify applied; if not, hint user
                    var verify = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity);
                    if (verify.Index == en.Index)
                    {
                        UpdateCameraValues();
                    }
                    else
                    {
                        MessageBox.Show("Camera ignored ISO change. Check Auto ISO or mode.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adjusting ISO: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("ISO adjustment error", ex);
            }
        }

        private void button_aperture_minus_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture);
                if (en == null) { MessageBox.Show("Aperture not available"); return; }
                if (en.Index > 0)
                {
                    en.Index = en.Index - 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture, en);
                    UpdateCameraValues();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adjusting Aperture: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("Aperture adjustment error", ex);
            }
        }

        private void button_aperture_plus_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture);
                if (en == null) { MessageBox.Show("Aperture not available"); return; }
                if (en.Index < en.Length - 1)
                {
                    en.Index = en.Index + 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture, en);
                    var verify = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture);
                    if (verify.Index == en.Index) UpdateCameraValues();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adjusting Aperture: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogError("Aperture adjustment error", ex);
            }
        }

        private void button_shutter_minus_Click(object sender, EventArgs e)
        {
            if (device == null || shutterButtonProcessing) return;
            shutterButtonProcessing = true;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed);
                if (en == null) { AppLogger.Warn("Shutter not available"); return; }
                if (en.Index > 0)
                {
                    en.Index = en.Index - 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed, en);
                    UpdateCameraValues();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Shutter Speed adjustment error", ex);
                LogError("Shutter Speed adjustment error", ex);
            }
            finally
            {
                shutterButtonProcessing = false;
            }
        }

        private void button_shutter_plus_Click(object sender, EventArgs e)
        {
            if (device == null || shutterButtonProcessing) return;
            shutterButtonProcessing = true;
            try
            {
                var en = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed);
                if (en == null) { AppLogger.Warn("Shutter not available"); shutterButtonProcessing = false; return; }
                if (en.Index < en.Length - 1)
                {
                    en.Index = en.Index + 1;
                    device.SetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed, en);
                    var verify = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed);
                    if (verify.Index == en.Index)
                    {
                        UpdateCameraValues();
                    }
                    else
                    {
                        AppLogger.Warn("Camera ignored shutter change. Switch to S/M mode and try again.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Shutter Speed adjustment error", ex);
                LogError("Shutter Speed adjustment error", ex);
            }
            finally
            {
                shutterButtonProcessing = false;
            }
        }

        private string FormatIso(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "---";
            return raw.StartsWith("ISO", StringComparison.OrdinalIgnoreCase) ? raw : ($"ISO {raw}");
        }

        private string FormatAperture(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "---";
            // If already like f/2.8 or F2.8, keep; else prefix f/
            if (raw.StartsWith("f/", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("F", StringComparison.Ordinal)) return raw;
            return $"f/{raw}";
        }

        private string FormatShutter(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "---";
            // If contains '/' it's already like 1/125. Else show raw (e.g., 2" for long exposes, or 0.5 s)
            return raw.Contains("/") ? raw : raw;
        }

        private void UpdateCameraValues()
        {
            if (device == null) return;

            try
            {
                // Update ISO display
                if (label_iso_value != null)
                {
                    try
                    {
                        NikonEnum isoEnum = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Sensitivity);
                        label_iso_value.Text = FormatIso(isoEnum != null ? isoEnum.ToString() : "");
                    }
                    catch { label_iso_value.Text = "Error"; }
                }

                // Update Aperture display
                if (label_aperture_value != null)
                {
                    try
                    {
                        NikonEnum apertureEnum = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_Aperture);
                        label_aperture_value.Text = FormatAperture(apertureEnum != null ? apertureEnum.ToString() : "");
                    }
                    catch { label_aperture_value.Text = "Error"; }
                }

                // Update Shutter Speed display
                if (label_shutter_value != null)
                {
                    try
                    {
                        NikonEnum shutterEnum = device.GetEnum(eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed);
                        label_shutter_value.Text = FormatShutter(shutterEnum != null ? shutterEnum.ToString() : "");
                    }
                    catch { label_shutter_value.Text = "Error"; }
                }

                // Update ExposureStatus (live meter EV)
                try
                {
                    float ev = (float)device.GetFloat(eNkMAIDCapability.kNkMAIDCapability_ExposureStatus);
                    currentExposureStatus = ev.ToString("F2");
                }
                catch (Exception ex)
                {
                    currentExposureStatus = "--";
                    AppLogger.Error($"ExposureStatus read failed: {ex.GetType().Name}: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                LogError("Error updating camera values", ex);
            }
        }

        private void UpdateExposurePreviewUI()
        {
            if (device == null) return;
            try
            {
                var cap = eNkMAIDCapability.kNkMAIDCapability_LiveViewExposurePreview;
                bool inLv = device.LiveViewEnabled;
                if (button_exposurePreview != null)
                {
                    button_exposurePreview.Enabled = inLv;
                    if (inLv)
                    {
                        try
                        {
                            var enumVal = device.GetEnum(cap);
                            if (enumVal != null)
                            {
                                bool isOn = (enumVal.Index == 1);
                                button_exposurePreview.Text = $"Exposure Preview: {(isOn ? "On" : "Off")}";
                            }
                            else
                            {
                                button_exposurePreview.Text = "Exposure Preview: --";
                            }
                        }
                        catch
                        {
                            button_exposurePreview.Text = "Exposure Preview: --";
                        }
                    }
                    else
                    {
                        button_exposurePreview.Text = "Exposure Preview: --";
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error updating exposure preview UI", ex);
                if (button_exposurePreview != null) button_exposurePreview.Enabled = false;
            }
        }

        private void button_exposurePreview_Click(object sender, EventArgs e)
        {
            if (device == null) return;
            try
            {
                var cap = eNkMAIDCapability.kNkMAIDCapability_LiveViewExposurePreview;
                if (!device.LiveViewEnabled) return;
                NikonEnum current = null;
                try
                {
                    current = device.GetEnum(cap);
                }
                catch (Exception)
                {
                    MessageBox.Show("Exposure Preview not supported in this mode.");
                    button_exposurePreview.Text = "Exposure Preview: --";
                    return;
                }
                if (current != null)
                {
                    // Toggle between 0 (Off) and 1 (On)
                    current.Index = (current.Index == 0) ? 1 : 0;
                    device.SetEnum(cap, current);
                    UpdateExposurePreviewUI();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to toggle Exposure Preview: {ex.Message}");
                LogError("Failed to toggle Exposure Preview", ex);
                UpdateExposurePreviewUI();
            }
        }

        private void button_meteringMode_Click(object sender, EventArgs e)
        {
            if (device == null)
            {
                MessageBox.Show("No camera connected");
                return;
            }
            try
            {
                AppLogger.Info("Metering mode button clicked");
                uint currentMode = device.GetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode);
                // D750 supports: 0=Matrix, 1=Center-weighted, 2=Spot (mode 3 causes out of bounds error)
                uint newMode = (currentMode + 1) % 3;
                AppLogger.Info($"Metering mode: old={currentMode}, new={newMode}");
                
                device.SetUnsigned(eNkMAIDCapability.kNkMAIDCapability_MeteringMode, newMode);
                AppLogger.Info($"SetUnsigned called");
                
                UpdateMeteringModeDisplay();
                button_meteringMode.Text = $"Metering: {currentMeteringMode}";
                AppLogger.Info($"Button updated to: {button_meteringMode.Text}");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"button_meteringMode_Click error: {ex.GetType().Name}: {ex.Message}", ex);
                MessageBox.Show($"Error: {ex.GetType().Name} - {ex.Message}", "Metering Mode Error");
            }
        }

        private void LogError(string message, Exception ex)
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"{DateTime.Now}: {message}");
                writer.WriteLine(ex.ToString());
                writer.WriteLine();
            }
        }

        // Enumerate capabilities and log any that look like Live View magnification/zoom controls
        private void ProbeLvZoomCapabilities()
        {
            if (device == null) return;
            var caps = device.GetCapabilityInfo();
            AppLogger.Info($"LV Zoom Probe: scanning {caps.Length} capabilities");
            foreach (var cap in caps)
            {
                // Description text may include keywords; also log all Enum/Unsigned types as candidates
                var desc = string.Empty;
                try { desc = cap.GetDescription(); } catch { }
                bool candidateType = cap.ulType == eNkMAIDCapType.kNkMAIDCapType_Unsigned || cap.ulType == eNkMAIDCapType.kNkMAIDCapType_Enum;
                bool keyword = (!string.IsNullOrEmpty(desc) && (desc.IndexOf("zoom", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("magnif", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("live view", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("lv", StringComparison.OrdinalIgnoreCase) >= 0));
                if (candidateType || keyword)
                {
                    AppLogger.Info($"CapID=0x{cap.ulID:X} Type={cap.ulType} CanGet={cap.CanGet()} CanSet={cap.CanSet()} Desc='{desc}'");
                    // If Enum, try to dump entries
                    if (cap.ulType == eNkMAIDCapType.kNkMAIDCapType_Enum)
                    {
                        try
                        {
                            var en = device.GetEnum((eNkMAIDCapability)cap.ulID);
                            if (en != null)
                            {
                                var values = new List<string>();
                                for (int i = 0; i < en.Length; i++)
                                {
                                    en.Index = i;
                                    values.Add(en.ToString());
                                }
                                AppLogger.Info($"  Enum values: [{string.Join(", ", values)}] currentIndex={en.Index}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"  Enum dump failed for 0x{cap.ulID:X}: {ex.Message}");
                        }
                    }
                }
            }
            AppLogger.Info("LV Zoom Probe: complete");
        }
    }
}
