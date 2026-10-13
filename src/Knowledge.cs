using System;
using System.Collections.Generic;

namespace TaskBoost
{
    // What TaskBoost knows by heart: common startup programs and keyboard makers
    static class Knowledge
    {
        // exe name without extension | K = keep, U = useful, O = optional | what it does
        const string Programs = @"
securityhealthsystray|K|Windows Security icon near the clock; shows Defender and firewall status
msmpeng|K|Windows Defender antivirus engine; scans files as they are opened
mpdefendercoreservice|K|Windows Defender core service; part of the built-in antivirus
nissrv|K|Windows Defender network inspection; watches network traffic for attacks
ctfmon|K|Windows text input helper; needed for typing, language switching and the touch keyboard
rtkauduservice64|K|Realtek audio helper; keeps sound effects, jack detection and the microphone working
rtkngui64|U|Realtek audio control panel in the tray
rtkbtmanserv|K|Realtek Bluetooth service; needed for Bluetooth devices
igfxtray|O|Intel graphics tray icon; display settings shortcuts only
igfxem|U|Intel graphics helper for hotkeys and display switching
igfxcuiservice|U|Intel graphics control panel service
igfxpers|O|Intel graphics settings persistence; restores display settings at sign-in
intelcphdcpsvc|U|Intel content protection service; needed to play some protected video
intelcphecisvc|U|Intel content protection service; needed to play some protected video
oneapp.igcc.winservice|O|Intel Graphics Command Center background service
lms|U|Intel Management Engine local service
jhi_service|U|Intel Dynamic Application Loader; part of the Intel Management Engine
esif_uf|K|Intel thermal framework; manages heat and fan behaviour, keep it on a laptop
ipf_uf|K|Intel Innovation Platform Framework; manages heat and power on a laptop
intelaudioservice|K|Intel Smart Sound service; needed for the microphone array and speakers
nvdisplay.container|K|NVIDIA display service; needed for the NVIDIA control panel and GPU switching
nvcontainer|U|NVIDIA background container; runs NVIDIA app features
nvbackend|O|NVIDIA GeForce Experience backend; game optimisation and driver updates
nvtray|O|NVIDIA tray icon
nvidia share|O|NVIDIA overlay for recording and screenshots
nvidia web helper|O|NVIDIA GeForce Experience helper
radeonsoftware|O|AMD Radeon control panel in the tray
lenovofnandfunctionkeys|K|Lenovo Fn and function keys; makes the brightness, volume and special keys work
lenovoutility|U|Lenovo utility for hotkeys and on-screen indicators
lenovoutilityservice|K|Lenovo Fn and function keys service; makes the brightness, volume and special keys work
wmiregistrationservice|U|Intel Management Engine component; registers hardware information for management tools
platformlicensemanagerservice|U|Intel licence service; used by protected video playback and some Intel features
lenovovantageservice|O|Lenovo Vantage background service; updates and device settings
legionfanscontroller|U|Lenovo Legion fan control
lsb|U|Lenovo Service Bridge; used by Lenovo's support site to detect the device
syntpenh|K|Synaptics touchpad helper; gestures and touchpad settings
etdctrl|K|ELAN touchpad helper; gestures and touchpad settings
windhawk|O|Windhawk; applies the Windows customisation mods you installed
powertoys|O|Microsoft PowerToys; extra Windows utilities
onedrive|O|Microsoft OneDrive; syncs your files to the cloud
teams|O|Microsoft Teams; starts in the background to receive calls and messages
ms-teams|O|Microsoft Teams; starts in the background to receive calls and messages
msedge|O|Microsoft Edge starting in the background so it opens faster
microsoftedgeupdate|U|Microsoft Edge updater; keeps Edge patched
chrome|O|Google Chrome starting in the background so it opens faster and can show notifications
googleupdate|U|Google updater; keeps Chrome patched
googleupdater|U|Google updater; keeps Chrome patched
googledrivefs|O|Google Drive; syncs your files to the cloud
dropbox|O|Dropbox; syncs your files to the cloud
discord|O|Discord chat; starts in the background to receive messages
slack|O|Slack chat; starts in the background to receive messages
skype|O|Skype; starts in the background to receive calls
zoom|O|Zoom; starts in the background for faster meetings
whatsapp|O|WhatsApp desktop; starts in the background to receive messages
telegram|O|Telegram desktop; starts in the background to receive messages
viber|O|Viber; starts in the background to receive messages and calls
spotify|O|Spotify music player
steam|O|Steam game store; starts in the background to update games
epicgameslauncher|O|Epic Games launcher; starts in the background to update games
riotclientservices|O|Riot Games client
vgtray|O|Riot Vanguard anti-cheat tray icon
battle.net|O|Blizzard Battle.net launcher
eadesktop|O|EA game launcher
upc|O|Ubisoft Connect launcher
galaxyclient|O|GOG Galaxy game launcher
acrotray|O|Adobe Acrobat tray helper; only needed for PDF printing shortcuts
adobearm|U|Adobe Reader and Acrobat updater
adobegcinvoker-1.0|O|Adobe licence check
creative cloud|O|Adobe Creative Cloud; syncs and updates Adobe apps
ccxprocess|O|Adobe Creative Cloud helper
jusched|U|Java updater
ituneshelper|O|iTunes helper; detects iPhones and iPods when plugged in
ccleaner64|O|CCleaner monitoring; not needed in the background
lghub|U|Logitech G HUB; applies mouse and keyboard profiles and lighting
logioptionsplus_agent|U|Logitech Options+; applies mouse and keyboard button settings
setpoint|U|Logitech SetPoint; applies mouse and keyboard button settings
razer synapse 3|U|Razer Synapse; applies device profiles and lighting
icue|U|Corsair iCUE; applies device profiles and lighting
steelseriesgg|U|SteelSeries GG; applies device profiles and lighting
msiafterburner|O|MSI Afterburner; GPU monitoring and overclocking
rtss|O|RivaTuner; on-screen frame rate display
hwinfo64|O|HWiNFO hardware monitor
docker desktop|O|Docker Desktop; runs containers in the background
com.docker.service|O|Docker background service
vmware-tray|O|VMware tray icon
vboxsvc|O|VirtualBox background service
sqlwriter|O|SQL Server backup helper; only needed when backing up SQL databases
tbtp2pshortcutservice|O|Thunderbolt PC-to-PC networking shortcut
ollama app|O|Ollama; runs local AI models in the background
claude|O|Claude desktop app
taskboost|K|TaskBoost itself, running in the tray: guard, desktop buddy and shortcuts
everything|O|Everything; instant file search
sharex|O|ShareX; screenshots and screen recording
lightshot|O|Lightshot; screenshots
greenshot|O|Greenshot; screenshots
flux|O|f.lux; warms the screen colour in the evening
utorrent|O|uTorrent; file sharing, uses the network in the background
qbittorrent|O|qBittorrent; file sharing, uses the network in the background
bittorrent|O|BitTorrent; file sharing, uses the network in the background
nordvpn|O|NordVPN; routes your traffic through a VPN
expressvpn|O|ExpressVPN; routes your traffic through a VPN
protonvpn|O|Proton VPN; routes your traffic through a VPN
openvpn-gui|O|OpenVPN; routes your traffic through a VPN
wireguard|O|WireGuard; routes your traffic through a VPN
anydesk|O|AnyDesk remote access; lets another device see and control this PC
teamviewer|O|TeamViewer remote access; lets another device see and control this PC
teamviewer_service|O|TeamViewer remote access service; lets another device connect to this PC
malwarebytes|U|Malwarebytes; anti-malware protection
mbamservice|U|Malwarebytes protection service
avastui|U|Avast antivirus
avgui|U|AVG antivirus
avp|U|Kaspersky antivirus
grammarly|O|Grammarly writing assistant
notion|O|Notion notes
phoneexperiencehost|O|Phone Link; connects your phone to the PC
cortana|O|Cortana assistant
msedgewebview2|U|Microsoft Edge WebView; used by many apps to show web content
widgetservice|O|Windows widgets
yourphone|O|Phone Link; connects your phone to the PC
xboxappservices|O|Xbox app background service
gamebarpresencewriter|O|Xbox Game Bar presence
nvidia broadcast|O|NVIDIA Broadcast; AI noise removal and camera effects
nvspcaps64|O|NVIDIA ShadowPlay capture helper
amdrsserv|O|AMD Radeon settings service
atieclxx|U|AMD external events helper; keeps display hotkeys working
rtkaudioservice64|K|Realtek audio service; keeps sound working
waves audio|O|Waves MaxxAudio sound effects
dolbydax3api|O|Dolby Atmos sound effects
nahimic|O|Nahimic sound effects
armourycrate|O|ASUS Armoury Crate; lighting and performance modes
asusoptimization|O|ASUS system optimisation helper
gaminghub|O|Samsung gaming hub
dell.d3.winsvc|O|Dell background service
dellsupportassistagent|O|Dell SupportAssist; driver updates and diagnostics
supportassistagent|O|Dell SupportAssist; driver updates and diagnostics
hpsupportsolutionsframeworkservice|O|HP Support Assistant background service
hpaudioswitch|O|HP audio switcher
omen command center|O|HP Omen control panel
acer care center|O|Acer Care Center
msi center|O|MSI Center; lighting and performance modes
dragon center|O|MSI Dragon Center
lenovo.vantage|O|Lenovo Vantage; updates and device settings
lenovonowlauncher|O|Lenovo offers at first start
lenovobatterygaugepackage|O|Lenovo battery gauge
ledkeeper2|O|Lenovo keyboard lighting
legionzone|O|Lenovo Legion Zone; performance modes
fnhotkeyutility|K|Fn hotkey utility; makes the function keys work
synaptics|K|Synaptics touchpad; gestures and settings
elan|K|ELAN touchpad; gestures and settings
wacom|U|Wacom tablet driver
huion|U|Huion tablet driver
xtuservice|O|Intel Extreme Tuning Utility
intel driver and support assistant|O|Intel driver updater
dsatray|O|Intel driver and support assistant tray icon
igfxdhlib|O|Intel graphics helper
onedrivesetup|O|OneDrive setup
onedrivestandaloneupdater|U|OneDrive updater
googledrive|O|Google Drive; syncs your files to the cloud
backup and sync|O|Google Backup and Sync
icloud|O|iCloud for Windows; photos and files from Apple devices
icloudservices|O|iCloud background service
applemobiledeviceservice|O|Apple device service; needed by iTunes to see iPhones
mega|O|MEGA cloud sync
megasync|O|MEGA cloud sync
pcloud|O|pCloud sync
nextcloud|O|Nextcloud sync
syncthing|O|Syncthing file sync
signal|O|Signal messenger
messenger|O|Facebook Messenger
teamsmeetingaddin|O|Teams meeting add-in
webexmta|O|Cisco Webex helper
webex|O|Cisco Webex meetings
goto opener|O|GoToMeeting helper
raidcall|O|RaidCall voice chat
ts3client|O|TeamSpeak voice chat
mumble|O|Mumble voice chat
twitch|O|Twitch desktop app
obs64|O|OBS Studio; streaming and recording
streamlabs obs|O|Streamlabs; streaming
xsplit|O|XSplit; streaming
vlc|O|VLC media player
itunes|O|iTunes
foobar2000|O|foobar2000 music player
musicbee|O|MusicBee music player
rainmeter|O|Rainmeter desktop widgets
wallpaperengine|O|Wallpaper Engine; animated wallpapers, uses GPU in the background
wallpaper32|O|Wallpaper Engine; animated wallpapers
wallpaper64|O|Wallpaper Engine; animated wallpapers
lively|O|Lively Wallpaper; animated wallpapers
startallback|O|StartAllBack; Start menu and taskbar tweaks
startisback|O|StartIsBack; Start menu tweaks
explorerpatcher|O|ExplorerPatcher; taskbar and Explorer tweaks
openshell|O|Open-Shell Start menu
classicstartmenu|O|Classic Shell Start menu
7+ taskbar tweaker|O|7+ Taskbar Tweaker
autohotkey|O|AutoHotkey scripts
autohotkeyu64|O|AutoHotkey scripts
ditto|O|Ditto clipboard manager
clipboardfusion|O|ClipboardFusion clipboard manager
displayfusion|O|DisplayFusion; multi-monitor tools
dualmonitortool|O|Dual Monitor Tools
f.lux|O|f.lux; warms the screen colour in the evening
twinkle tray|O|Twinkle Tray; monitor brightness control
ms-settings|O|Windows settings link
keepass|O|KeePass password manager
keepassxc|O|KeePassXC password manager
bitwarden|O|Bitwarden password manager
1password|O|1Password password manager
lastpass|O|LastPass password manager
dashlane|O|Dashlane password manager
nordpass|O|NordPass password manager
proton drive|O|Proton Drive sync
protonmail bridge|O|Proton Mail Bridge
thunderbird|O|Thunderbird email
outlook|O|Microsoft Outlook
mailbird|O|Mailbird email
evernote|O|Evernote notes
obsidian|O|Obsidian notes
todoist|O|Todoist tasks
trello|O|Trello boards
asana|O|Asana tasks
clickup|O|ClickUp tasks
figma_agent|O|Figma font helper
fontbase|O|FontBase font manager
adobe desktop service|O|Adobe Creative Cloud background service
adobeipcbroker|O|Adobe Creative Cloud helper
adobeupdateservice|U|Adobe updater
coreSync|O|Adobe Creative Cloud file sync
nikcollection|O|Nik Collection helper
epson|O|Epson printer helper
canon|O|Canon printer helper
hp print|O|HP printer helper
brother|O|Brother printer helper
sunlogin|O|Sunlogin remote access; lets another device control this PC
todesk|O|ToDesk remote access; lets another device control this PC
rustdesk|O|RustDesk remote access; lets another device control this PC
parsec|O|Parsec remote access; lets another device control this PC
chrome remote desktop host|O|Chrome Remote Desktop; lets another device control this PC
remoting_host|O|Chrome Remote Desktop host
splashtop|O|Splashtop remote access
logmein|O|LogMeIn remote access
vmware workstation|O|VMware Workstation
vmnetdhcp|O|VMware virtual network DHCP
vmware-authd|O|VMware authorisation service
vboxheadless|O|VirtualBox virtual machine
wsl|O|Windows Subsystem for Linux
wslservice|O|Windows Subsystem for Linux service
python|O|A Python script set to run at start; check the command line
node|O|A Node.js script set to run at start; check the command line
java|O|A Java program set to run at start; check the command line
javaw|O|A Java program set to run at start; check the command line
cmd|O|A command script set to run at start; read the command line
powershell|O|A PowerShell script set to run at start; read the command line
wscript|O|A Windows script set to run at start; read the command line
mshta|O|An HTML application set to run at start; rarely legitimate
rundll32|O|Runs code from a DLL at start; read the command line
regsvr32|O|Registers a DLL at start; read the command line
schtasks|O|Creates or runs a scheduled task at start; read the command line
curl|O|Downloads something at start; read the command line
bitsadmin|O|Downloads something at start; read the command line
certutil|O|Often misused to download files; read the command line
";

        // USB vendor id | maker
        const string Vendors = @"
048D|ITE (built-in laptop keyboard)
04F2|Chicony (built-in or OEM keyboard)
04CA|Lite-On (built-in or OEM keyboard)
17EF|Lenovo
03F0|HP
413C|Dell
0B05|ASUS
1462|MSI
04E8|Samsung
05AC|Apple
045E|Microsoft
046D|Logitech
1532|Razer
1B1C|Corsair
1038|SteelSeries
0951|HyperX / Kingston
3434|Keychron
04D9|Holtek (budget keyboards)
258A|SINO WEALTH (budget keyboards)
0C45|Sonix (budget keyboards)
1A2C|China Resource Semico (budget keyboards)
24AE|Rapoo
2341|Arduino (programmable board)
2A03|Arduino (programmable board)
16C0|Teensy / V-USB (programmable board)
239A|Adafruit (programmable board)
1B4F|SparkFun (programmable board)
2E8A|Raspberry Pi Pico (programmable board)
03EB|Atmel (programmable board, used by USB Rubber Ducky)
16D0|Digispark (programmable board)
1209|pid.codes (hobby and open-source hardware)
";

        static Dictionary<string, string[]> programs, vendors;

        static Dictionary<string, string[]> Parse(string table)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in table.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = line.Split('|');
                map[p[0]] = p;
            }
            return map;
        }

        public static bool Program(string exeName, out string importance, out string what)
        {
            if (programs == null) programs = Parse(Programs);
            string[] p;
            bool found = programs.TryGetValue(exeName ?? "", out p);
            importance = found ? p[1] : null;
            what = found ? p[2] : null;
            return found;
        }

        // id looks like VID_046D&PID_C31C
        public static string KeyboardName(string id)
        {
            if (vendors == null) vendors = Parse(Vendors);
            string[] v;
            if (id.Length >= 8 && id.StartsWith("VID_") && vendors.TryGetValue(id.Substring(4, 4), out v)) return v[1] + " [" + id + "]";
            return "Unknown maker [" + id + "]";
        }

        // Keystroke-injection sticks are built on these hobby boards
        public static bool ProgrammableBoard(string id)
        {
            return KeyboardName(id).Contains("programmable board");
        }
    }
}
