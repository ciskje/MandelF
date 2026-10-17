namespace MandelbrotViewer;

partial class BenchmarkForm
{
    private System.ComponentModel.IContainer components = null!;
    private Label lblInfo = null!;
    private Label lblResult = null!;
    private Label lblDetail = null!;
    private Panel chartPanel = null!;
    private Panel bottomPanel = null!;
    private Label lblLive = null!;
    private Button btnStart = null!;
    private Button btnClose = null!;
    private Button btnCsv = null!;
    private PictureBox previewBox = null!;

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
        this.lblResult = new Label();
        this.lblDetail = new Label();
        this.chartPanel = new Panel();
        this.bottomPanel = new Panel();
        this.lblLive = new Label();
        this.btnStart = new Button();
        this.btnClose = new Button();
        this.btnCsv = new Button();
        this.previewBox = new PictureBox();

        this.bottomPanel.SuspendLayout();
        this.SuspendLayout();

        // lblInfo
        this.lblInfo.Dock = DockStyle.Top;
        this.lblInfo.Height = 56;
        this.lblInfo.Padding = new Padding(12, 10, 12, 0);
        this.lblInfo.ForeColor = System.Drawing.Color.DimGray;

        // lblResult: the test number, large
        this.lblResult.Dock = DockStyle.Fill;
        this.lblResult.Text = "—";
        this.lblResult.Font = new System.Drawing.Font("Segoe UI", 24f, System.Drawing.FontStyle.Bold);
        this.lblResult.TextAlign = ContentAlignment.MiddleCenter;

        // lblDetail
        this.lblDetail.Dock = DockStyle.Bottom;
        this.lblDetail.Height = 28;
        this.lblDetail.Text = "";
        this.lblDetail.TextAlign = ContentAlignment.MiddleCenter;
        this.lblDetail.ForeColor = System.Drawing.Color.DimGray;
        this.lblDetail.Font = new System.Drawing.Font("Segoe UI", 8f);

        // previewBox: first frame of the benchmark zone (for CUDA/CPU)
        this.previewBox.Dock = DockStyle.Top;
        this.previewBox.Height = 190;
        this.previewBox.BackColor = System.Drawing.Color.Black;
        this.previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        this.previewBox.Visible = false;

        // chartPanel
        this.chartPanel.Dock = DockStyle.Bottom;
        this.chartPanel.Height = 205;
        this.chartPanel.Padding = new Padding(12, 0, 12, 4);
        this.chartPanel.BackColor = System.Drawing.Color.White;
        this.chartPanel.Paint += new PaintEventHandler(this.ChartPanel_Paint);

        // bottomPanel
        this.bottomPanel.Dock = DockStyle.Bottom;
        this.bottomPanel.Height = 92;
        this.bottomPanel.Padding = new Padding(12, 8, 12, 10);
        this.bottomPanel.Controls.Add(this.lblLive);
        this.bottomPanel.Controls.Add(this.btnStart);
        this.bottomPanel.Controls.Add(this.btnCsv);
        this.bottomPanel.Controls.Add(this.btnClose);
        // lblLive
        this.lblLive.Dock = DockStyle.Fill;
        this.lblLive.Text = "";
        this.lblLive.TextAlign = ContentAlignment.MiddleCenter;

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

        // btnCsv
        this.btnCsv.Dock = DockStyle.Right;
        this.btnCsv.Width = 108;
        this.btnCsv.Text = "Export CSV";
        this.btnCsv.Enabled = false;
        this.btnCsv.Click += new EventHandler(this.BtnCsv_Click);

        // BenchmarkForm
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(520, 647);
        this.Controls.Add(this.lblResult);
        this.Controls.Add(this.lblDetail);
        this.Controls.Add(this.lblInfo);
        this.Controls.Add(this.previewBox);
        this.Controls.Add(this.chartPanel);
        this.Controls.Add(this.bottomPanel);
        this.Text = "Benchmark";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.CancelButton = this.btnClose;

        this.bottomPanel.ResumeLayout(false);
        this.ResumeLayout(false);
    }
}
