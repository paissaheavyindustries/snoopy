using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SnoopyClient
{

    public partial class UserInterface : UserControl
    {

        internal bool ShuttingDown { get; set; } = false;
        private DateTime LastLogSnapshot = DateTime.MinValue;

        private List<Engine.LogEvent> logsnap = new List<Engine.LogEvent>();

        private Engine _eng = null;
        internal Engine eng
        {
            get
            {
                return _eng;
            }
            set
            {
                if (ShuttingDown == true)
                {
                    return;
                }
                if (_eng != value)
                {
                    if (_eng != null)
                    {
                        _eng.OnStateChange -= _e_OnStateChange;
                    }
                    _eng = value;
                    if (_eng != null)
                    {                        
                        _eng.OnStateChange += _e_OnStateChange;
                        ApplyState(Engine.EngineStateEnum.Undefined, eng.State);
                        cbxMode.SelectedIndex = (int)_eng.cfg._OperationMode;
                        txtHost.Text = _eng.cfg.Host;
                        txtDumpDate.Text = _eng.cfg.DumpDateFormat;
                        nudPort.Value = _eng.cfg.Port;
                        chkAutoStart.Checked = _eng.cfg.AutoStart;
                        chkDumpNetwork.Checked = _eng.cfg.DumpNetwork;
                        chkDumpParsed.Checked = _eng.cfg.DumpParsed;
                        txtNetworkFolder.Text = _eng.cfg.DumpFolderNetwork;
                        txtParsedFolder.Text = _eng.cfg.DumpFolderParsed;
                    }
                    else
                    {
                        ApplyState(Engine.EngineStateEnum.Undefined, Engine.EngineStateEnum.Undefined);
                    }                    
                }
            }
        }

        private void _e_OnStateChange(Engine.EngineStateEnum oldstate, Engine.EngineStateEnum newstate)
        {
            ApplyState(oldstate, newstate);
        }

        public UserInterface()
        {
            InitializeComponent();
        }

        private void ApplyState(Engine.EngineStateEnum oldstate, Engine.EngineStateEnum newstate)
        {
            if (ShuttingDown == true)
            {
                return;
            }
            if (btnStart.InvokeRequired == true)
            {
                btnStart.Invoke(new Engine.StateChangeDelegate(ApplyState), oldstate, newstate);
                return;
            }
            switch (newstate)
            {
                case Engine.EngineStateEnum.Started:
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                    nudPort.Enabled = false;
                    txtHost.Enabled = false;
                    txtDumpDate.Enabled = false;
                    cbxMode.Enabled = false;
                    txtNetworkFolder.Enabled = false;
                    txtParsedFolder.Enabled = false;
                    btnBrowseNetwork.Enabled = false;
                    btnBrowseParsed.Enabled = false;
                    break;
                case Engine.EngineStateEnum.Stopping:
                case Engine.EngineStateEnum.Starting:
                case Engine.EngineStateEnum.Undefined:
                    btnStart.Enabled = false;
                    btnStop.Enabled = false;
                    nudPort.Enabled = false;
                    txtHost.Enabled = false;
                    txtDumpDate.Enabled = false;
                    cbxMode.Enabled = false;
                    txtNetworkFolder.Enabled = false;
                    txtParsedFolder.Enabled = false;
                    btnBrowseNetwork.Enabled = false;
                    btnBrowseParsed.Enabled = false;
                    break;
                case Engine.EngineStateEnum.Stopped:
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    nudPort.Enabled = true;
                    txtHost.Enabled = (_eng.cfg._OperationMode == Engine.EngineModeEnum.Client);
                    txtDumpDate.Enabled = true;
                    cbxMode.Enabled = true;
                    txtNetworkFolder.Enabled = true;
                    txtParsedFolder.Enabled = true;
                    btnBrowseNetwork.Enabled = true;
                    btnBrowseParsed.Enabled = true;
                    break;
            }
        }

        private void timer1_Tick(object sender, System.EventArgs e)
        {
            txtBytesReceived.Text = eng != null ? eng.StatsBytesReceived.ToString() : "Undefined";
            txtEventsQueued.Text = eng != null ? eng.StatsEventsInQueue.ToString() : "Undefined";
            txtNetworkSeen.Text = eng != null ? eng.StatsEventsNetworkSeen.ToString() : "Undefined";
            txtParsedSeen.Text = eng != null ? eng.StatsEventsParsedSeen.ToString() : "Undefined";
            txtReceivesDone.Text = eng != null ? eng.StatsReceivesTriggered.ToString() : "Undefined";
            txtNetworkState.Text = eng != null ? eng.StatsNetworkState.ToString() : "Undefined";
            lblNewEvents.Visible = eng != null ? eng.StatsLatestLogEvent > LastLogSnapshot : false;
            btnRefreshLog.Enabled = lblNewEvents.Visible;
        }

        private void btnStart_Click(object sender, System.EventArgs e)
        {
            eng.Start();
        }

        private void btnStop_Click(object sender, System.EventArgs e)
        {
            eng.Stop();
        }

        private void cbxMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            _eng.cfg._OperationMode = (Engine.EngineModeEnum)cbxMode.SelectedIndex;
            txtHost.Enabled = (_eng.cfg._OperationMode == Engine.EngineModeEnum.Client);
        }

        private void txtHost_TextChanged(object sender, EventArgs e)
        {
            _eng.cfg.Host = txtHost.Text;
        }

        private void nudPort_ValueChanged(object sender, EventArgs e)
        {
            _eng.cfg.Port = (int)nudPort.Value;
        }

        private void chkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            _eng.cfg.AutoStart = chkAutoStart.Checked;
        }

        private void chkDumpParsed_CheckedChanged(object sender, EventArgs e)
        {
            _eng.cfg.DumpParsed = chkDumpParsed.Checked;
        }

        private void chkDumpNetwork_CheckedChanged(object sender, EventArgs e)
        {
            _eng.cfg.DumpNetwork = chkDumpNetwork.Checked;
        }

        private void txtParsedFolder_TextChanged(object sender, EventArgs e)
        {
            _eng.cfg.DumpFolderParsed = txtParsedFolder.Text;
        }

        private void txtNetworkFolder_TextChanged(object sender, EventArgs e)
        {
            _eng.cfg.DumpFolderNetwork = txtNetworkFolder.Text;
        }

        private void txtDumpDate_TextChanged(object sender, EventArgs e)
        {
            _eng.cfg.DumpDateFormat = txtDumpDate.Text;
        }

        private void btnBrowseParsed_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.SelectedPath = txtParsedFolder.Text;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                txtParsedFolder.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void btnBrowseNetwork_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.SelectedPath = txtNetworkFolder.Text;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                txtNetworkFolder.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void btnRefreshLog_Click(object sender, EventArgs e)
        {            
            List<Engine.LogEvent> evs = new List<Engine.LogEvent>();
            lock (_eng.log)
            {
                evs.AddRange(_eng.log);
                lblNewEvents.Visible = false;
                btnRefreshLog.Enabled = false;
                LastLogSnapshot = DateTime.Now;
            }
            logsnap.Clear();
            logsnap.AddRange(evs);
            logsnap.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            dgvLog.RowCount = logsnap.Count;
            dgvLog.Invalidate();
        }

        private void dgvLog_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            Engine.LogEvent le = logsnap[e.RowIndex];
            switch (le.Type)
            {
                case Engine.LogEventTypeEnum.Debug:
                    e.CellStyle.BackColor = Color.LightGray;
                    e.CellStyle.ForeColor = Color.Black;
                    break;
                case Engine.LogEventTypeEnum.Info:
                    e.CellStyle.BackColor = Color.White;
                    e.CellStyle.ForeColor = Color.Black;
                    break;
                case Engine.LogEventTypeEnum.Warning:
                    e.CellStyle.BackColor = Color.Yellow;
                    e.CellStyle.ForeColor = Color.Black;
                    break;
                case Engine.LogEventTypeEnum.Error:
                    e.CellStyle.BackColor = Color.Red;
                    e.CellStyle.ForeColor = Color.Yellow;
                    break;
            }
            e.FormattingApplied = true;
        }

        private void dgvLog_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            Engine.LogEvent le = logsnap[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0:
                    e.Value = le.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"); ;
                    break;
                case 1:
                    e.Value = le.Type.ToString();
                    break;
                case 2:
                    e.Value = le.Description;
                    break;
            }
        }

    }

}
