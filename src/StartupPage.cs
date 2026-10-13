using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskBoost
{
    // The "Startup" tab: every program that starts with Windows, what it does, a verdict, and an on/off switch
    class StartupPage : Panel
    {
        readonly Label caption = Ui.Caption("EVERYTHING THAT STARTS WITH WINDOWS, AND WHAT IT DOES");
        readonly Label summary = new Label();
        readonly Panel listPanel = new Panel();
        readonly FindingList list = new FindingList(3);
        readonly FlatButton btnSwitch = new FlatButton(), btnRescan = new FlatButton(), btnLocate = new FlatButton();
        bool scanning, loaded;

        public StartupPage()
        {
            BackColor = Theme.Bg;
            summary.AutoSize = true;
            summary.Font = Theme.Body;
            summary.ForeColor = Theme.Muted;
            summary.BackColor = Theme.Bg;
            Controls.Add(caption);
            Controls.Add(summary);

            listPanel.BackColor = Theme.LogBg;
            listPanel.Padding = new Padding(Theme.Px(6));
            list.Dock = DockStyle.Fill;
            list.SelectedIndexChanged += delegate { RefreshSwitch(); };
            listPanel.Controls.Add(list);
            Controls.Add(listPanel);

            btnSwitch.Primary = true;
            btnRescan.Text = "Scan again";
            btnLocate.Text = "Open file location";
            btnSwitch.Click += delegate { Switch(); };
            btnRescan.Click += delegate { Rescan(null); };
            btnLocate.Click += delegate
            {
                var f = list.SelectedItem as Finding;
                if (f != null && f.Path != null) Process.Start("explorer.exe", "/select,\"" + f.Path + "\"");
            };
            Controls.AddRange(new Control[] { btnSwitch, btnRescan, btnLocate });
            RefreshSwitch();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !loaded) Rescan(null);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gap = Theme.Px(10), w = ClientSize.Width, bh = Theme.Px(36);
            caption.Location = new Point(0, 0);
            summary.Location = new Point(0, Theme.Px(20));
            int y = Theme.Px(44), by = ClientSize.Height - bh;
            listPanel.SetBounds(0, y, w, Math.Max(Theme.Px(60), by - Theme.Px(14) - y));
            btnSwitch.SetBounds(0, by, Theme.Px(150), bh);
            btnRescan.SetBounds(btnSwitch.Right + gap, by, Theme.Px(130), bh);
            btnLocate.SetBounds(btnRescan.Right + gap, by, Theme.Px(170), bh);
        }

        void RefreshSwitch()
        {
            var f = list.SelectedItem as Finding;
            btnSwitch.Text = f != null && f.Off ? "Switch on" : "Switch off";
        }

        // Turns the selected item off or back on. Nothing is deleted: sign-in entries use the same switch as
        // Task Manager, services go between Automatic and Manual.
        void Switch()
        {
            var f = list.SelectedItem as Finding;
            if (f == null)
            {
                Dialog.Show(this, "Select an item in the list first.");
                return;
            }
            if (f.Ref == null)
            {
                Dialog.Show(this, "This entry runs only once and then removes itself, so it has no on/off switch.");
                return;
            }
            bool on = f.Off, service = f.Ref[0] == "service";
            if (!on)
            {
                string question = f.Tag == "Keep"
                    ? Lang.T("TaskBoost marks this one as needed:") + "\n\n" + f.Title + "\n" + f.Detail + "\n\n" + Lang.T("Switch it off anyway?")
                    : service ? Lang.T("Stop this background service from starting with Windows?") + "\n\n" + f.Title + "\n\n" + Lang.T("It keeps running until the next restart. You can switch it back on here.")
                    : null;
                if (question != null && !Dialog.Ask(this, question)) return;
            }

            string key = f.Key;
            if (!service && f.Ref[1] == "HKCU")
            {
                // The user's own entries need no administrator rights
                StartupInfo.SetEnabled(f.Ref, on);
                Rescan(key);
                return;
            }
            string target = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", f.Ref)));
            SetBusy(true);
            Task.Factory.StartNew(delegate { return Helper.Run("startup-set --target " + target + " --on " + (on ? "1" : "0")); }).ContinueWith(delegate(Task<string> t)
            {
                SetBusy(false);
                string problem = t.Exception != null ? t.Exception.InnerException.Message : t.Result.Contains("ERROR") ? t.Result.Trim() : null;
                if (problem != null) Dialog.Show(this, Lang.T("It could not be switched:") + "\n\n" + problem);
                Rescan(key);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void SetBusy(bool busy)
        {
            foreach (Control c in Controls)
                if (c is DarkControl) c.Enabled = !busy;
            btnSwitch.Busy = busy;
        }

        void Rescan(string selectKey)
        {
            if (scanning) return;
            scanning = loaded = true;
            if (selectKey == null) summary.Text = Lang.T("Looking at what starts with Windows...");
            btnRescan.Busy = true;
            Task.Factory.StartNew(delegate { return StartupInfo.Scan(); }).ContinueWith(delegate(Task<List<Finding>> t)
            {
                scanning = false;
                btnRescan.Busy = false;
                if (t.Exception != null)
                {
                    summary.Text = Lang.T("The scan failed: ") + t.Exception.InnerException.Message;
                    return;
                }
                int suspicious = 0, check = 0, off = 0, top = list.TopIndex;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var f in t.Result)
                {
                    if (f.Level == 2) suspicious++;
                    if (f.Level == 1) check++;
                    if (f.Off) off++;
                    list.Items.Add(f);
                    if (f.Key == selectKey) list.SelectedIndex = list.Items.Count - 1;
                }
                if (selectKey != null && top < list.Items.Count) list.TopIndex = top;
                list.EndUpdate();
                if (selectKey == null) list.Reveal();
                RefreshSwitch();
                summary.Text = t.Result.Count + Lang.T(" items:  ") + suspicious + Lang.T(" suspicious,  ") + check + Lang.T(" to check,  ") + off + Lang.T(" switched off,  ")
                    + (t.Result.Count - suspicious - check - off) + Lang.T(" fine.  Select one and use the switch button below.");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
