namespace HikrobotCameraProcessor
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            comboBoxCameras = new ComboBox();
            groupBox1 = new GroupBox();
            bDisconnect = new Button();
            bConnect = new Button();
            button1 = new Button();
            gboxParams = new GroupBox();
            panelIO = new FlowLayoutPanel();
            btnExportConfig = new Button();
            btnImportConfig = new Button();
            btnSystemControl = new Button();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // comboBoxCameras
            // 
            comboBoxCameras.FormattingEnabled = true;
            comboBoxCameras.Location = new Point(21, 21);
            comboBoxCameras.Name = "comboBoxCameras";
            comboBoxCameras.Size = new Size(506, 23);
            comboBoxCameras.TabIndex = 0;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(bDisconnect);
            groupBox1.Controls.Add(bConnect);
            groupBox1.Controls.Add(button1);
            groupBox1.Controls.Add(comboBoxCameras);
            groupBox1.Location = new Point(12, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(1057, 61);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Select Camera";
            // 
            // bDisconnect
            // 
            bDisconnect.Enabled = false;
            bDisconnect.Location = new Point(751, 21);
            bDisconnect.Name = "bDisconnect";
            bDisconnect.Size = new Size(85, 27);
            bDisconnect.TabIndex = 3;
            bDisconnect.Text = "Disconnect";
            bDisconnect.UseVisualStyleBackColor = true;
            bDisconnect.Click += bDisconnect_Click;
            // 
            // bConnect
            // 
            bConnect.Location = new Point(632, 21);
            bConnect.Name = "bConnect";
            bConnect.Size = new Size(85, 27);
            bConnect.TabIndex = 2;
            bConnect.Text = "Connect";
            bConnect.UseVisualStyleBackColor = true;
            bConnect.Click += bConnect_Click;
            // 
            // button1
            // 
            button1.Location = new Point(533, 20);
            button1.Name = "button1";
            button1.Size = new Size(75, 28);
            button1.TabIndex = 1;
            button1.Text = "Refresh";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // gboxParams
            // 
            gboxParams.Location = new Point(8, 315);
            gboxParams.Name = "gboxParams";
            gboxParams.Size = new Size(1061, 229);
            gboxParams.TabIndex = 4;
            gboxParams.TabStop = false;
            gboxParams.Text = "I/O Handler";
            // 
            // panelIO
            // 
            panelIO.Location = new Point(12, 79);
            panelIO.Name = "panelIO";
            panelIO.Size = new Size(1057, 230);
            panelIO.TabIndex = 0;
            // 
            // btnExportConfig
            // 
            btnExportConfig.Location = new Point(949, 550);
            btnExportConfig.Name = "btnExportConfig";
            btnExportConfig.Size = new Size(116, 23);
            btnExportConfig.TabIndex = 5;
            btnExportConfig.Text = "Export config";
            btnExportConfig.UseVisualStyleBackColor = true;
            btnExportConfig.Click += btnExportConfig_Click;
            // 
            // btnImportConfig
            // 
            btnImportConfig.Location = new Point(826, 550);
            btnImportConfig.Name = "btnImportConfig";
            btnImportConfig.Size = new Size(117, 23);
            btnImportConfig.TabIndex = 6;
            btnImportConfig.Text = "Import config";
            btnImportConfig.UseVisualStyleBackColor = true;
            btnImportConfig.Click += btnImportConfig_Click;
            // 
            // btnSystemControl
            // 
            btnSystemControl.Location = new Point(12, 552);
            btnSystemControl.Name = "btnSystemControl";
            btnSystemControl.Size = new Size(232, 29);
            btnSystemControl.TabIndex = 7;
            btnSystemControl.Text = "ЗАПУСТИТЬ МОНИТОРИНГ";
            btnSystemControl.UseVisualStyleBackColor = true;
            btnSystemControl.Click += btnSystemControl_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1077, 587);
            Controls.Add(btnSystemControl);
            Controls.Add(btnImportConfig);
            Controls.Add(btnExportConfig);
            Controls.Add(panelIO);
            Controls.Add(gboxParams);
            Controls.Add(groupBox1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "Hikrobot Camera Processor | Support: MV-CE / MV-CH Series";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            groupBox1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private ComboBox comboBoxCameras;
        private GroupBox groupBox1;
        private Button button1;
        private Button bDisconnect;
        private Button bConnect;
        private GroupBox gboxParams;
        private FlowLayoutPanel panelIO;
        private Button btnExportConfig;
        private Button btnImportConfig;
        private Button btnSystemControl;
    }
}
