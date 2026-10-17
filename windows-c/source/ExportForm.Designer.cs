namespace MandelbrotViewer;

partial class ExportForm
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblInfo = null!;
    private Panel rowPreset = null!;
    private Label lblPreset = null!;
    private ComboBox cmbPreset = null!;
    private Panel rowCustom = null!;
    private Label lblCustom = null!;
    private TextBox txtW = null!;
    private Label lblX = null!;
    private TextBox txtH = null!;
    private Panel rowAA = null!;
    private Label lblAA = null!;
    private ComboBox cmbAAExp = null!;
    private Panel rowPrec = null!;
    private Label lblPrec = null!;
    private RadioButton radPrec32 = null!;
    private RadioButton radPrec64 = null!;
    private ProgressBar progressBar = null!;
    private Label lblResult = null!;
    private Panel bottomPanel = null!;
    private Button btnStart = null!;
    private Button btnClose = null!;

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
        this.rowPreset = new Panel();
        this.lblPreset = new Label();
        this.cmbPreset = new ComboBox();
        this.rowCustom = new Panel();
        this.lblCustom = new Label();
        this.txtW = new TextBox();
        this.lblX = new Label();
        this.txtH = new TextBox();
        this.rowAA = new Panel();
        this.lblAA = new Label();
        this.cmbAAExp = new ComboBox();
        this.rowPrec = new Panel();
        this.lblPrec = new Label();
        this.radPrec32 = new RadioButton();
        this.radPrec64 = new RadioButton();
        this.progressBar = new ProgressBar();
        this.lblResult = new Label();
        this.bottomPanel = new Panel();
        this.btnStart = new Button();
        this.btnClose = new Button();

        this.rowPreset.SuspendLayout();
        this.rowCustom.SuspendLayout();
        this.rowAA.SuspendLayout();
        this.rowPrec.SuspendLayout();
        this.bottomPanel.SuspendLayout();
        this.SuspendLayout();

        // lblInfo
        this.lblInfo.Dock = DockStyle.Top;
        this.lblInfo.Height = 64;
        this.lblInfo.Padding = new Padding(12, 10, 12, 0);
        this.lblInfo.ForeColor = System.Drawing.Color.DimGray;

        // rowPreset
        this.rowPreset.Dock = DockStyle.Top;
        this.rowPreset.Height = 34;
        this.rowPreset.Padding = new Padding(12, 4, 12, 4);
        this.rowPreset.Controls.Add(this.cmbPreset);
        this.rowPreset.Controls.Add(this.lblPreset);
        // lblPreset
        this.lblPreset.Dock = DockStyle.Left;
        this.lblPreset.Width = 150;
        this.lblPreset.Text = "Preset:";
        this.lblPreset.TextAlign = ContentAlignment.MiddleLeft;
        // cmbPreset
        this.cmbPreset.Dock = DockStyle.Fill;
        this.cmbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbPreset.Items.AddRange(new object[] {
            "Current view",
            "Full HD (1920×1080)",
            "2K (2560×1440)",
            "4K (3840×2160)",
            "8K (7680×4320)",
            "Double 4K (7680×2160)",
            "Custom…" });
        this.cmbPreset.SelectedIndex = 3;
        this.cmbPreset.SelectedIndexChanged += new EventHandler(this.CmbPreset_Changed);

        // rowCustom
        this.rowCustom.Dock = DockStyle.Top;
        this.rowCustom.Height = 34;
        this.rowCustom.Padding = new Padding(12, 4, 12, 4);
        this.rowCustom.Controls.Add(this.txtH);
        this.rowCustom.Controls.Add(this.lblX);
        this.rowCustom.Controls.Add(this.txtW);
        this.rowCustom.Controls.Add(this.lblCustom);
        // lblCustom
        this.lblCustom.Dock = DockStyle.Left;
        this.lblCustom.Width = 150;
        this.lblCustom.Text = "Width × height:";
        this.lblCustom.TextAlign = ContentAlignment.MiddleLeft;
        // txtW
        this.txtW.Dock = DockStyle.Left;
        this.txtW.Width = 80;
        this.txtW.TextChanged += new EventHandler(this.CmbWidth_Changed);
        // lblX
        this.lblX.Dock = DockStyle.Left;
        this.lblX.Width = 24;
        this.lblX.Text = "×";
        this.lblX.TextAlign = ContentAlignment.MiddleCenter;
        // txtH
        this.txtH.Dock = DockStyle.Left;
        this.txtH.Width = 80;
        this.txtH.TextChanged += new EventHandler(this.CmbWidth_Changed);

        // rowAA
        this.rowAA.Dock = DockStyle.Top;
        this.rowAA.Height = 34;
        this.rowAA.Padding = new Padding(12, 4, 12, 4);
        this.rowAA.Controls.Add(this.cmbAAExp);
        this.rowAA.Controls.Add(this.lblAA);
        // lblAA
        this.lblAA.Dock = DockStyle.Left;
        this.lblAA.Width = 150;
        this.lblAA.Text = "Antialiasing:";
        this.lblAA.TextAlign = ContentAlignment.MiddleLeft;
        // cmbAAExp
        this.cmbAAExp.Dock = DockStyle.Fill;
        this.cmbAAExp.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbAAExp.Items.AddRange(new object[] { "As view", "1x", "2x", "4x", "8x" });
        this.cmbAAExp.SelectedIndex = 0;
        this.cmbAAExp.SelectedIndexChanged += new EventHandler(this.CmbWidth_Changed);

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
        this.progressBar.Style = ProgressBarStyle.Marquee;
        this.progressBar.Visible = false;

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

        // ExportForm
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(420, 344);
        this.Controls.Add(this.lblResult);
        this.Controls.Add(this.progressBar);
        this.Controls.Add(this.rowPrec);
        this.Controls.Add(this.rowAA);
        this.Controls.Add(this.rowCustom);
        this.Controls.Add(this.rowPreset);
        this.Controls.Add(this.lblInfo);
        this.Controls.Add(this.bottomPanel);
        this.Text = "Export high-resolution PNG";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.CancelButton = this.btnClose;

        this.rowPreset.ResumeLayout(false);
        this.rowCustom.ResumeLayout(false);
        this.rowAA.ResumeLayout(false);
        this.rowPrec.ResumeLayout(false);
        this.bottomPanel.ResumeLayout(false);
        this.ResumeLayout(false);
    }
}
