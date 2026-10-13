using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TaskBoost
{
    // Routine maintenance, done with Windows' own tools. Nothing here deletes user data.
    static class Care
    {
        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        // Time since Windows last started
        public static TimeSpan Uptime { get { return TimeSpan.FromMilliseconds(GetTickCount64()); } }

        public static void Run(ICollection<string> items)
        {
            Log.Info("--- maintenance started ---");
            if (items.Contains("health")) Step("Health report", Health);
            if (!App.IsElevated && (items.Contains("chkdsk") || items.Contains("optimize") || items.Contains("sfc") || items.Contains("dism")))
                Log.Info("Not administrator: disk check, drive optimisation, system file check and Windows image check were skipped.");
            else
            {
                string system = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                if (items.Contains("chkdsk")) Tool("Disk check (" + system + ")", "chkdsk.exe", system + " /scan", null);
                if (items.Contains("optimize")) Tool("Drive optimisation (TRIM on SSDs, defragmentation on hard disks)", "defrag.exe", "/C /O", null);
                if (items.Contains("sfc")) Tool("System file check", "sfc.exe", "/scannow", Encoding.Unicode);
                if (items.Contains("dism"))
                {
                    Tool("Windows image check", "dism.exe", "/Online /Cleanup-Image /ScanHealth", null);
                    Tool("Windows image clean-up", "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", null);
                }
            }
            if (items.Contains("defender")) Step("Quick virus scan", Defender);
            Log.Info("--- maintenance finished ---");
        }

        static void Step(string title, Action work)
        {
            Log.Info(title + "...");
            try { work(); }
            catch (Exception ex) { Log.Info(title + ": could not run (" + ex.Message + ")"); }
        }

        // Runs a Windows tool and logs the last lines of what it said, without the progress noise
        static void Tool(string title, string exe, string args, Encoding encoding)
        {
            Log.Info(title + ": running, this can take several minutes...");
            try
            {
                string output;
                int rc = Util.Run(exe, args, out output, encoding);
                var lines = new List<string>();
                foreach (string raw in output.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length > 0 && line.IndexOf('%') < 0 && !Regex.IsMatch(line, @"^[=\[\]\-\s\.]+$")) lines.Add(line);
                }
                // Prefer the lines that state a result; fall back to the last few
                var verdicts = lines.FindAll(delegate(string l) { return Regex.IsMatch(l, "no problems|found|corrupt|integrity|repair|successfully|error|trimmed", RegexOptions.IgnoreCase); });
                if (verdicts.Count > 0) lines = verdicts;
                int from = Math.Max(0, lines.Count - 3);
                for (int i = from; i < lines.Count; i++) Log.Info("   " + lines[i]);
                Log.Info(title + ": " + (rc == 0 ? "done" : "finished with code " + rc));
            }
            catch (Exception ex) { Log.Info(title + ": could not run (" + ex.Message + ")"); }
        }

        static void Health()
        {
            var up = Uptime;
            Log.Info("   Running for " + (up.TotalHours < 1 ? up.Minutes + " minutes" : (int)up.TotalDays + " days " + up.Hours + " hours") + " since the last restart" + (up.TotalDays >= 7 ? "  -  a restart would help" : ""));

            bool reboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null
                       || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;
            if (reboot) Log.Info("   Windows is waiting for a restart to finish installing something");

            Log.Info("   Memory: " + Memory.AvailableMb() + " MB free");
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                double free = d.AvailableFreeSpace / 1073741824.0, total = d.TotalSize / 1073741824.0;
                int percent = (int)(free * 100 / total);
                Log.Info("   Drive " + d.Name.TrimEnd('\\') + " " + free.ToString("0") + " GB free of " + total.ToString("0") + " GB (" + percent + "%)" + (percent < 10 ? "  -  LOW, free some space" : ""));
            }

            string output;
            if (Util.Run("powershell.exe", "-NoProfile -Command \"Get-PhysicalDisk | ForEach-Object { $_.FriendlyName + ' | ' + $_.MediaType + ' | ' + $_.HealthStatus }\"", out output) == 0)
                foreach (string line in output.Split('\n'))
                    if (line.Trim().Length > 0) Log.Info("   Disk: " + line.Trim() + (line.Contains("Healthy") ? "" : "  -  NOT HEALTHY, back up your files"));

            Battery();
        }

        // Battery wear from Windows' own battery report: how much of the original capacity is left
        static void Battery()
        {
            string file = Path.Combine(App.DataDir, "battery.xml"), output;
            if (Util.Run("powercfg.exe", "/batteryreport /xml /output \"" + file + "\"", out output) != 0 || !File.Exists(file)) return;
            string xml = File.ReadAllText(file);
            File.Delete(file);
            var design = Regex.Match(xml, @"<DesignCapacity>(\d+)</DesignCapacity>");
            var full = Regex.Match(xml, @"<FullChargeCapacity>(\d+)</FullChargeCapacity>");
            var cycles = Regex.Match(xml, @"<CycleCount>(\d+)</CycleCount>");
            if (!design.Success || !full.Success || design.Groups[1].Value == "0")
            {
                Log.Info("   Battery: none (desktop PC)");
                return;
            }
            double left = double.Parse(full.Groups[1].Value) * 100 / double.Parse(design.Groups[1].Value);
            Log.Info("   Battery: holds " + left.ToString("0") + "% of its original capacity" + (cycles.Success && cycles.Groups[1].Value != "0" ? ", " + cycles.Groups[1].Value + " charge cycles" : "")
                + (left < 60 ? "  -  worn, expect short battery life" : ""));
        }

        static void Defender()
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            if (!File.Exists(exe))
            {
                Log.Info("   Windows Defender is not installed on this PC");
                return;
            }
            string output;
            int rc = Util.Run(exe, "-Scan -ScanType 1", out output, null);
            Log.Info(rc == 0 ? "   Quick virus scan: no threats found" : rc == 2 ? "   Quick virus scan: THREATS FOUND or the scan could not finish. Open Windows Security to see the details" : "   Quick virus scan: finished with code " + rc);
        }
    }
}
