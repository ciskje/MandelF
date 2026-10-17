namespace MandelbrotViewer;

partial class ZoomVideoForm
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblInfo = null!;
    private Panel rowPanel = null!;
    private Label lblFrames = null!;
    private ComboBox cmbFrames = null!;
    private Label lblFps = null!;
    private ComboBox cmbFps = null!;
    private Panel rowAA = null!;
    private Label lblAA = null!;
    private ComboBox cmbAAVid = null!;
    private Panel rowPrec = null!;
    private Label lblPrec = null!;
    private RadioButton radPrec32 = null!;
    private RadioButton radPrec64 = null!;
    private ProgressBar progressBar = null!;
    private Label lblResult = null!;
    private Panel bottomPanel = null!;
    private Button btnStart = null!;
    private Button btnClose = null!;
    private Button btnOpen = null!;


    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.lblInfo = new Label();
        this.rowPanel = new Panel();
        this.lblFrames = new Label();
        this.cmbFrames = new ComboBox();
        this.lblFps = new Label();
        this.cmbFps = new ComboBox();
        this.rowAA = new Panel();
        this.lblAA = new Label();
        this.cmbAAVid = new ComboBox();
        this.rowPrec = new Panel();
        this.lblPrec = new Label();
        this.radPrec32 = new RadioButton();
        this.radPrec64 = new RadioButton();
        this.progressBar = new ProgressBar();
        this.lblResult = new Label();
        this.bottomPanel = new Panel();
        this.btnStart = new Button();
        this.btnClose = new Button();
        this.btnOpen = new Button();

        this.rowPanel.SuspendLayout();
        this.rowAA.SuspendLayout();
        this.rowPrec.SuspendLayout();
        this.bottomPanel.SuspendLayout();
        this.SuspendLayout();

        // lblInfo
        this.lblInfo.Dock = DockStyle.Top;
        this.lblInfo.Height = 64;
        this.lblInfo.Padding = new Padding(12, 10, 12, 0);
        this.lblInfo.ForeColor = System.Drawing.Color.DimGray;

        // rowPanel
        this.rowPanel.Dock = DockStyle.Top;
        this.rowPanel.Height = 34;
        this.rowPanel.Padding = new Padding(12, 4, 12, 4);
        this.rowPanel.Controls.Add(this.cmbFps);
        this.rowPanel.Controls.Add(this.lblFps);
        this.rowPanel.Controls.Add(this.cmbFrames);
        this.rowPanel.Controls.Add(this.lblFrames);
        // lblFrames
        this.lblFrames.Dock = DockStyle.Left;
        this.lblFrames.Width = 55;
        this.lblFrames.Text = "Frame:";
        this.lblFrames.TextAlign = ContentAlignment.MiddleLeft;
        // cmbFrames
        this.cmbFrames.Dock = DockStyle.Left;
        this.cmbFrames.Width = 80;
        this.cmbFrames.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbFrames.Items.AddRange(new object[] { "60", "120", "240", "480" });
        this.cmbFrames.SelectedIndex = 1;
        // lblFps
        this.lblFps.Dock = DockStyle.Left;
        this.lblFps.Width = 55;
        this.lblFps.Text = "   FPS:";
        this.lblFps.TextAlign = ContentAlignment.MiddleLeft;
        // cmbFps
        this.cmbFps.Dock = DockStyle.Left;
        this.cmbFps.Width = 80;
        this.cmbFps.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbFps.Items.AddRange(new object[] { "24", "30", "60" });
        this.cmbFps.SelectedIndex = 1;

        // rowAA
        this.rowAA.Dock = DockStyle.Top;
        this.rowAA.Height = 34;
        this.rowAA.Padding = new Padding(12, 4, 12, 4);
        this.rowAA.Controls.Add(this.cmbAAVid);
        this.rowAA.Controls.Add(this.lblAA);
        // lblAA
        this.lblAA.Dock = DockStyle.Left;
        this.lblAA.Width = 150;
        this.lblAA.Text = "Antialiasing:";
        this.lblAA.TextAlign = ContentAlignment.MiddleLeft;
        // cmbAAVid
        this.cmbAAVid.Dock = DockStyle.Fill;
        this.cmbAAVid.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbAAVid.Items.AddRange(new object[] { "As view", "1x", "2x", "4x", "8x" });
        this.cmbAAVid.SelectedIndex = 0;
        this.cmbAAVid.SelectedIndexChanged += new EventHandler(this.CmbAAVid_Changed);

        // rowPrec
        this.rowPrec.Dock = DockStyle.Top;
        this.rowPrec.Height = 34;
        this.rowPrec.Padding = new Padding(12, 4, 12, 4);
        this.rowPrec.Controls.Add(this.radPrec64);
        this.rowPrec.Controls.Add(this.radPrec32);
        this.rowPrec.Controls.Add(this.lblPrec);
        // lblPrec
        this.lblPrec.Dock = DockStyle.Left;
        this.lblPrec.Width = 150;
        this.lblPrec.Text = "Precision:";
        this.lblPrec.TextAlign = ContentAlignment.MiddleLeft;
        // radPrec32
        this.radPrec32.Dock = DockStyle.Left;
        this.radPrec32.AutoSize = true;
        this.radPrec32.Text = "32-bit";
        this.radPrec32.CheckedChanged += new EventHandler(this.Prec_CheckedChanged);
        // radPrec64
        this.radPrec64.Dock = DockStyle.Left;
        this.radPrec64.AutoSize = true;
        this.radPrec64.Text = "64-bit (slow)";
        this.radPrec64.CheckedChanged += new EventHandler(this.Prec_CheckedChanged);

        // progressBar
        this.progressBar.Dock = DockStyle.Top;
        this.progressBar.Height = 16;

        // lblResult
        this.lblResult.Dock = DockStyle.Fill;
        this.lblResult.Padding = new Padding(12, 6, 12, 0);
        this.lblResult.ForeColor = System.Drawing.Color.DimGray;
        this.lblResult.Text = "";

        // bottomPanel
        this.bottomPanel.Dock = DockStyle.Bottom;
        this.bottomPanel.Height = 46;
        this.bottomPanel.Padding = new Padding(12, 8, 12, 10);
        this.bottomPanel.Controls.Add(this.btnStart);
        this.bottomPanel.Controls.Add(this.btnOpen);
        this.bottomPanel.Controls.Add(this.btnClose);
        // btnStart
        this.btnStart.Dock = DockStyle.Right;
        this.btnStart.Width = 90;
        this.btnStart.Text = "Start";
        this.btnStart.Click += new EventHandler(this.BtnStart_Click);
        // btnClose
        this.btnClose.Dock = DockStyle.Right;
        this.btnClose.Width = 90;
        this.btnClose.Text = "Close";
        this.btnClose.DialogResult = DialogResult.Cancel;
        this.btnClose.Click += new EventHandler(this.BtnClose_Click);

        // btnOpen
        this.btnOpen.Dock = DockStyle.Right;
        this.btnOpen.Width = 90;
        this.btnOpen.Text = "Open…";
        this.btnOpen.Enabled = false;
        this.btnOpen.Click += new EventHandler(this.BtnOpen_Click);

        // ZoomVideoForm
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(420, 318);
        this.Controls.Add(this.lblResult);
        this.Controls.Add(this.progressBar);
        this.Controls.Add(this.rowPrec);
        this.Controls.Add(this.rowAA);
        this.Controls.Add(this.rowPanel);
        this.Controls.Add(this.lblInfo);
        this.Controls.Add(this.bottomPanel);
        this.Text = "Export zoom video";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.CancelButton = this.btnClose;

        this.rowPanel.ResumeLayout(false);
        this.rowAA.ResumeLayout(false);
        this.rowPrec.ResumeLayout(false);
        this.bottomPanel.ResumeLayout(false);
        this.ResumeLayout(false);
    }
}
