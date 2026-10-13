using System;
using System.IO;
using Microsoft.Win32;

namespace TaskBoost
{
    // Looks online for what this particular device needs: BIOS and firmware from the maker, drivers and Windows
    // updates from Windows Update, antivirus definitions. It installs through Windows Update only; it never
    // flashes a BIOS itself.
    static class Updates
    {
        const string WuKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        static int Flag(string subKey, string name)
        {
            object v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + WuKey + subKey, name, 0);
            return v == null ? 0 : Convert.ToInt32(v);
        }

        // True when automatic updates are off, by TaskBoost's switch or by a redirect to an update server
        public static bool Stopped { get { return Flag(@"\AU", "NoAutoUpdate") == 1 || Flag(@"\AU", "UseWUServer") == 1; } }

        public static void Stop()
        {
            using (var au = Registry.LocalMachine.CreateSubKey(WuKey + @"\AU")) au.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
            Log.Info("Windows updates: automatic updates are stopped. Security fixes stop arriving too, so check by hand now and then.");
        }

        public static void Allow()
        {
            using (var au = Registry.LocalMachine.CreateSubKey(WuKey + @"\AU"))
            using (var wu = Registry.LocalMachine.CreateSubKey(WuKey))
            {
                au.DeleteValue("NoAutoUpdate", false);
                string server = wu.GetValue("WUServer") as string;
                if (Convert.ToInt32(au.GetValue("UseWUServer", 0)) == 1 && server != null)
                {
                    if (Resolves(server)) Log.Info("this PC gets its updates from an organisation's server (" + server + "); that was left as it is");
                    else
                    {
                        // A common way to block updates: point Windows Update at a server that does not exist
                        string backup = Path.Combine(App.DataDir, "windows-update-block-backup.reg"), output;
                        Util.Run("reg.exe", "export \"HKLM\\" + WuKey + "\" \"" + backup + "\" /y", out output);
                        au.DeleteValue("UseWUServer", false);
                        foreach (string name in new[] { "WUServer", "WUStatusServer", "UpdateServiceUrlAlternate", "DoNotConnectToWindowsUpdateInternetLocations" })
                            wu.DeleteValue(name, false);
                        Log.Info("removed a block that pointed Windows Update at a server that does not exist (" + server + "); a copy of it is in " + backup);
                    }
                }
            }
            Log.Info("Windows updates: allowed again");
        }

        static bool Resolves(string url)
        {
            try
            {
                System.Net.Dns.GetHostEntry(new Uri(url.Contains("://") ? url : "http://" + url).Host);
                return true;
            }
            catch (Exception) { return false; }
        }

        // The maker's own driver and firmware page for this model
        public static string MakerPage()
        {
            const string bios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
            string maker = Convert.ToString(Registry.GetValue(bios, "SystemManufacturer", "")), product = Convert.ToString(Registry.GetValue(bios, "SystemProductName", ""));
            string family = Convert.ToString(Registry.GetValue(bios, "SystemFamily", ""));
            if (maker.ToUpperInvariant().Contains("LENOVO") && product.Length >= 4)
                return "https://pcsupport.lenovo.com/products/" + Uri.EscapeDataString(product.Substring(0, 4)) + "/downloads/driver-list";
            return "https://www.bing.com/search?q=" + Uri.EscapeDataString(maker + " " + (family.Length > 0 ? family : product) + " " + product + " support drivers BIOS download");
        }

        const string DeviceScript = @"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$b = Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
'Device: ' + $b.SystemManufacturer + ' ' + $b.SystemFamily + ' (' + $b.SystemProductName + ')'
'BIOS installed: ' + $b.BIOSVersion + ', dated ' + $b.BIOSReleaseDate
if ($b.SystemManufacturer -match 'LENOVO') {
  try {
    $ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36'
    $p = Invoke-RestMethod ('https://pcsupport.lenovo.com/us/en/api/v4/mse/getproducts?productId=' + $b.SystemProductName.Substring(0, 4)) -UserAgent $ua -TimeoutSec 25
    $r = Invoke-RestMethod ('https://pcsupport.lenovo.com/us/en/api/v4/downloads/drivers?productId=' + $p[0].Id) -UserAgent $ua -Headers @{ Referer = 'https://pcsupport.lenovo.com/' } -TimeoutSec 45
    foreach ($i in ($r.body.DownloadItems | Where-Object { $_.Category.Name -match 'BIOS|Firmware' })) {
      $f = $i.Files | Where-Object { $_.URL -match '\.exe$' } | Select-Object -First 1
      $line = 'Maker offers: ' + ($i.Title -replace ' for Windows.*$', '') + ', version ' + $f.Version + ', ' + [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$f.Date.Unix).ToString('yyyy-MM-dd')
      if ($i.Title -match '^BIOS') { if ($f.Version -eq $b.BIOSVersion) { $line += '  -  you already have it' } else { $line += '  -  DIFFERENT FROM YOURS: get it from the maker''s driver page' } }
      $line
    }
  } catch { 'The maker''s catalogue could not be read: ' + $_.Exception.Message }
} else { 'For BIOS and firmware from ' + $b.SystemManufacturer + ', use the Maker''s driver page button.' }
";

        const string SearchScript = @"
try {
  $s = New-Object -ComObject Microsoft.Update.Session
  foreach ($kind in 'Software', 'Driver') {
    $r = $s.CreateUpdateSearcher().Search('IsInstalled=0 and IsHidden=0 and Type=''' + $kind + '''')
    if ($r.Updates.Count -eq 0) { 'Windows Update: no ' + $kind.ToLower() + ' updates waiting' }
    foreach ($u in $r.Updates) {
      $label = if ($kind -eq 'Driver') { if ($u.DriverClass -eq 'Firmware') { 'FIRMWARE' } else { 'DRIVER' } } else { 'WINDOWS' }
      $label + ' update waiting: ' + $u.Title
    }
  }
} catch { 'Windows Update could not be checked (' + $_.Exception.Message.Trim() + '). If Stop Windows updates is on, switch it off and check again.' }
try { $d = Get-MpComputerStatus -ErrorAction Stop; 'Antivirus definitions: ' + $d.AntivirusSignatureVersion + ', ' + [int]((Get-Date) - $d.AntivirusSignatureLastUpdated).TotalDays + ' days old' } catch { }
";

        const string InstallScript = @"
try {
  $s = New-Object -ComObject Microsoft.Update.Session
  $all = New-Object -ComObject Microsoft.Update.UpdateColl
  foreach ($kind in 'Software', 'Driver') {
    $r = $s.CreateUpdateSearcher().Search('IsInstalled=0 and IsHidden=0 and Type=''' + $kind + '''')
    foreach ($u in $r.Updates) { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$all.Add($u) }
  }
  if ($all.Count -eq 0) { 'Windows Update: nothing to install' }
  else {
    'Downloading ' + $all.Count + ' update(s)...'
    $d = $s.CreateUpdateDownloader(); $d.Updates = $all; [void]$d.Download()
    $i = $s.CreateUpdateInstaller(); $i.Updates = $all; $res = $i.Install()
    for ($n = 0; $n -lt $all.Count; $n++) { $code = $res.GetUpdateResult($n).ResultCode; ($(if ($code -eq 2) { 'Installed: ' } else { 'NOT installed (code ' + $code + '): ' })) + $all.Item($n).Title }
    if ($res.RebootRequired) { 'Restart the PC to finish installing.' }
  }
} catch { 'Windows Update could not install (' + $_.Exception.Message.Trim() + '). If Stop Windows updates is on, switch it off and try again.' }
";

        static void RunScript(string script)
        {
            string output;
            Util.PowerShell(script, out output);
            foreach (string line in output.Split('\n'))
                if (line.Trim().Length > 0 && !line.StartsWith("#<") && !line.StartsWith("<Objs")) Log.Info("   " + line.Trim());
        }

        public static void Check()
        {
            Log.Info("Looking online for what this device needs. This can take a minute...");
            RunScript(DeviceScript);
            RunScript(SearchScript);
            Log.Info("Update check finished.");
        }

        public static void Install()
        {
            Log.Info("Installing what Windows Update offers (drivers and firmware included). This can take a while...");
            RunScript(InstallScript);
            string defender = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe"), output;
            if (File.Exists(defender)) Log.Info("   Antivirus definitions: " + (Util.Run(defender, "-SignatureUpdate", out output) == 0 ? "up to date" : "could not be updated"));
            Log.Info("Update run finished. BIOS updates from the maker are not installed automatically: use the maker's driver page for those.");
        }
    }
}
