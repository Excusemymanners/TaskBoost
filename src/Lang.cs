using System;
using System.Collections.Generic;
using System.IO;

namespace TaskBoost
{
    // Interface language. English is what the code says; Romanian is looked up here at draw time,
    // so a string with no translation simply stays in English.
    static class Lang
    {
        public static bool Romanian;

        static string FilePath { get { return Path.Combine(App.DataDir, "lang.txt"); } }

        public static void Load()
        {
            try { Romanian = File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "ro"; }
            catch (IOException) { Romanian = false; }
        }

        public static void Save(bool romanian)
        {
            File.WriteAllText(FilePath, romanian ? "ro" : "en");
            Romanian = romanian;
        }

        // For the tests: when set, every text that was asked for and has no translation is noted here
        public static HashSet<string> Missed = null;

        public static string T(string s)
        {
            if (!Romanian || s == null) return s;
            string r;
            if (Ro.TryGetValue(s, out r)) return r;
            if (Missed != null) lock (Missed) Missed.Add(s);
            return s;
        }

        static readonly Dictionary<string, string> Ro = new Dictionary<string, string>
        {
            // tabs and header
            { "Home", "Acasă" }, { "Guard", "Gardă" }, { "Startup", "Pornire" }, { "Care", "Îngrijire" },
            { "Turbo: on", "Turbo: pornit" }, { "Ask Claude", "Întreabă Claude" },
            { "Fast, clean and guarded. All quiet.", "Rapid, curat și păzit. Totul e liniștit." },
            { " on the Guard tab", " pe ecranul Gardă" }, { " alert", " alertă" }, { " alerts", " alerte" }, { " notice", " atenționare" }, { " notices", " atenționări" },

            // Boost
            { "EVERYDAY TWEAKS", "SETĂRI DE ZI CU ZI" }, { "NEEDS ADMINISTRATOR APPROVAL", "CER APROBARE DE ADMINISTRATOR" }, { "WHEN TO RUN", "CÂND SĂ RULEZE" },
            { "Dark mode", "Mod întunecat" }, { "Solid black desktop, no transparency", "Desktop negru, fără transparență" },
            { "No animations", "Fără animații" }, { "Window, menu and taskbar effects off", "Efecte de ferestre, meniuri și bară oprite" },
            { "Quiet background", "Fundal liniștit" }, { "No background apps, tips or startup delay", "Fără aplicații de fundal, sugestii sau întârziere la pornire" },
            { "Thin taskbar", "Bară subțire" }, { "Small icons, window titles, no search box", "Iconițe mici, titluri de ferestre, fără căutare" },
            { "Battery first", "Bateria întâi" }, { "Short timeouts, CPU capped at 99% on battery", "Stingere rapidă, procesor limitat la 99% pe baterie" },
            { "No browser autostart", "Fără browsere la start" }, { "Chrome and Edge stay closed at sign-in", "Chrome și Edge rămân închise la logare" },
            { "Trim services", "Servicii reduse" }, { "Telemetry, Xbox, maps and fax off", "Telemetrie, Xbox, hărți și fax oprite" },
            { "Text boot", "Pornire cu text" }, { "Driver list at boot, detailed sign-in status", "Lista driverelor la pornire, stare detaliată la logare" },
            { "Bare Start menu", "Meniu Start gol" }, { "No tiles; the layout is locked", "Fără tiles; aranjarea e blocată" },
            { "Manual", "Manual" }, { "Only when I press Apply now, e.g. before a heavy task", "Doar când apăs Aplică acum, înaintea unui task greu" },
            { "Automatic", "Automat" }, { "Applies the switched-on items at every sign-in", "Aplică elementele pornite la fiecare logare" },
            { "Apply now", "Aplică acum" }, { "My full setup", "Setare completă" }, { "Restore previous settings", "Readu setările vechi" }, { "Open log folder", "Deschide jurnalul" },
            { "Working...", "Lucrez..." },

            // Guard
            { "WHAT TO WATCH", "CE SĂ URMĂREASCĂ" }, { "HOW IT BEHAVES", "CUM SE POARTĂ" }, { "RIGHT NOW", "CHIAR ACUM" },
            { "Camera and mic", "Cameră și microfon" }, { "Shows which apps are using them, or your location", "Arată ce aplicații le folosesc, sau locația ta" },
            { "Network activity", "Activitate de rețea" }, { "Programs online now; flags unsigned and new ones", "Programe online acum; semnalează cele nesemnate și noi" },
            { "Fake keyboards", "Tastaturi false" }, { "A new keyboard, or two connected at once", "O tastatură nouă, sau două conectate deodată" },
            { "Remote and mirroring", "Control la distanță" }, { "Remote Desktop, remote tools, extra screens", "Remote Desktop, unelte de la distanță, ecrane extra" },
            { "New startup programs", "Programe noi la pornire" }, { "Alerts when something adds itself to startup", "Alertă când ceva se adaugă singur la pornire" },
            { "Suspicious signs", "Semne suspecte" }, { "Disguised programs, proxy, hosts file, Defender off", "Programe deghizate, proxy, fișier hosts, Defender oprit" },
            { "Notifications", "Notificări" }, { "Pop up a warning when an alert appears", "Avertizare când apare o alertă" },
            { "Stay in the tray", "Rămâne în tray" }, { "Keep watching after the window is closed", "Continuă paza după închiderea ferestrei" },
            { "Start with Windows", "Pornește cu Windows" }, { "Start the guard hidden at every sign-in", "Pornește garda ascuns la fiecare logare" },
            { "Desktop buddy", "Păianjenul" }, { "A pixel spider that walks the screen edges", "Un păianjen pixelat care merge pe marginile ecranului" },
            { "Hide while I work", "Se ascunde cât lucrez" }, { "Only on desktop, browsers, Explorer, terminals", "Doar pe desktop, browsere, Explorer, terminale" },
            { "Only with Claude", "Doar cu Claude" }, { "Appears while Claude is connected (MCP)", "Apare cât e Claude conectat (MCP)" },
            { "Mark as known", "Marchează cunoscut" }, { "Mark all as known", "Toate cunoscute" }, { "End program", "Închide programul" }, { "Open file location", "Arată fișierul" },
            { "Connecting...", "Mă conectez..." },

            // Startup
            { "EVERYTHING THAT STARTS WITH WINDOWS, AND WHAT IT DOES", "TOT CE PORNEȘTE CU WINDOWS ȘI CE FACE FIECARE" },
            { "Switch off", "Oprește" }, { "Switch on", "Pornește" }, { "Scan again", "Scanează din nou" },
            { "Looking at what starts with Windows...", "Mă uit ce pornește cu Windows..." },

            // Turbo
            { "LAPTOP MODE", "MODUL LAPTOPULUI" }, { "GAMING MODE", "MOD GAMING" }, { "MY GAMES  (get the fast graphics card and high priority)", "JOCURILE MELE  (primesc placa video rapidă și prioritate)" },
            { "SAFE CLEAN-UP", "CURĂȚARE SIGURĂ" },
            { "Low power", "Consum redus" }, { "Normal", "Normal" }, { "Study / work", "Studiu / lucru" }, { "Best performance", "Performanță maximă" }, { "Gaming", "Gaming" },
            { "Close extras", "Închide extra" }, { "Sync tools, updaters and phone link", "Sincronizări, actualizatoare și Phone Link" },
            { "Close browsers", "Închide browserele" }, { "Browsers and chat apps; open pages are lost", "Browsere și chat; paginile deschise se pierd" },
            { "Pause services", "Pauză servicii" }, { "Updates, indexing and telemetry, until you leave", "Actualizări, indexare și telemetrie, până ieși" },
            { "Enter gaming mode", "Intră în gaming" }, { "Leave gaming mode", "Ieși din gaming" }, { "Add a game", "Adaugă un joc" }, { "Remove game", "Scoate jocul" },
            { "Free memory now", "Eliberează memoria" }, { "Charge my phone", "Încarcă telefonul" }, { "End charging mode", "Oprește modul de încărcare" },
            { "Temporary files", "Fișiere temporare" }, { "Your temp folder, files older than a week", "Folderul tău temp, fișiere mai vechi de o săptămână" },
            { "Windows temp files", "Temp Windows" }, { "The Windows temp folder, older than a week", "Folderul temp al Windows, mai vechi de o săptămână" },
            { "Crash reports", "Rapoarte de erori" }, { "Old error reports and crash dumps", "Rapoarte vechi de erori și crash dumps" },
            { "Network buffers", "Bufere de rețea" }, { "Flushes the DNS cache", "Golește cache-ul DNS" },
            { "Clipboard", "Clipboard" }, { "Empties whatever was copied last", "Golește ultimul lucru copiat" },
            { "Recycle Bin", "Coș de reciclare" }, { "Empties it for good, so it is off by default", "Îl golește definitiv, de aceea e oprit implicit" },
            { "Preview clean-up", "Previzualizare" }, { "Clean now", "Curăță acum" },
            { "Gaming mode changes are temporary: leaving it puts everything back.", "Schimbările din gaming sunt temporare: la ieșire totul revine." },

            // Care
            { "MAINTENANCE", "MENTENANȚĂ" }, { "UPDATES FOR THIS DEVICE", "ACTUALIZĂRI PENTRU ACEST DISPOZITIV" },
            { "Health report", "Raport de sănătate" }, { "Disks, free space, battery wear, uptime", "Discuri, spațiu liber, uzura bateriei, timp de funcționare" },
            { "Check the disk", "Verifică discul" }, { "Scans the system drive for errors", "Caută erori pe unitatea de sistem" },
            { "Optimise drives", "Optimizează unitățile" }, { "TRIM on SSDs, defrag on hard disks", "TRIM pe SSD, defragmentare pe hard disk" },
            { "Quick virus scan", "Scanare rapidă de viruși" }, { "Windows Defender quick scan, a few minutes", "Scanare rapidă Defender, câteva minute" },
            { "Check system files", "Verifică fișierele de sistem" }, { "Repairs damaged Windows files, 10 to 20 minutes", "Repară fișierele Windows stricate, 10-20 minute" },
            { "Windows image", "Imaginea Windows" }, { "Checks and tidies the component store, slow", "Verifică și curăță depozitul de componente, lent" },
            { "Run maintenance", "Rulează mentenanța" }, { "The PC stays usable while this runs. Nothing here deletes your files.", "PC-ul rămâne utilizabil. Nimic de aici nu șterge fișierele tale." },
            { "Stop updates", "Oprește actualizările" }, { "Windows stops updating itself until you switch this off", "Windows nu se mai actualizează singur până oprești asta" },
            { "Check for updates", "Caută actualizări" }, { "Install updates", "Instalează actualizări" }, { "Maker's driver page", "Pagina producătorului" },
            { "Pick what to run. A monthly run of the first four is plenty for most PCs.", "Alege ce să ruleze. O dată pe lună primele patru ajung pentru majoritatea PC-urilor." },

            // Home
            { "HEALTH SCORE", "SCOR DE SĂNĂTATE" }, { "WHAT MAKES UP THE SCORE", "DIN CE E FORMAT SCORUL" }, { "SETTINGS", "SETĂRI" },
            { "Recalculate", "Recalculează" }, { "Export settings", "Exportă setările" }, { "Import settings", "Importă setările" },
            { "Language: English", "Limba: Română" }, { "Remove TaskBoost", "Dezinstalare" },
            { "Excellent", "Excelent" }, { "Good", "Bine" }, { "Needs attention", "Are nevoie de atenție" }, { "In trouble", "Are probleme" },
            { "Calculating...", "Calculez..." },
            { "Security", "Securitate" }, { "Startup programs", "Programe la pornire" }, { "Disk space", "Spațiu pe disc" }, { "Memory", "Memorie" },
            { "Protection", "Protecție" }, { "Clean-up", "Curățenie" }, { "Updates", "Actualizări" },

            // buddy menus and sayings
            { "Emotes", "Expresii" }, { "Search for files...", "Caută fișiere..." }, { "Open TaskBoost", "Deschide TaskBoost" }, { "Take a nap", "Trage un pui de somn" }, { "Go away", "Pleacă" },
            { "Low power  -  longest battery (sleepy spider)", "Consum redus  -  baterie cât mai lungă (păianjen somnoros)" },
            { "Normal  -  everyday balance", "Normal  -  echilibru de zi cu zi" },
            { "Study / work  -  quick apps, quiet, easy on battery (engineer)", "Studiu / lucru  -  aplicații rapide, liniște, baterie (inginer)" },
            { "Best performance  -  full power (red spider)", "Performanță maximă  -  putere totală (păianjen roșu)" },
            { "Gaming  -  full power, background paused (asks approval)", "Gaming  -  putere totală, fundal pe pauză (cere aprobare)" },
            { "Net focus: this program goes first online", "Net focus: programul ăsta e primul la internet" }, { "Just sit here", "Doar stau aici" },
            { "Keyboard shortcuts", "Scurtături de tastatură" }, { "Ask Claude about this PC", "Întreabă Claude despre PC" },
            { "Send the buddy away", "Trimite păianjenul" }, { "Call the buddy back", "Cheamă păianjenul" }, { "Exit", "Ieșire" },
            { "Still here in the tray. Right-click the spider icon to exit.", "Rămân aici în tray. Click dreapta pe păianjen ca să ieși." },
            { "Claude is connected. I'm on watch!", "Claude s-a conectat. Sunt de pază!" }, { "Claude disconnected. See you!", "Claude s-a deconectat. Pe curând!" },
            { "Low power: saving battery. Zzz...", "Consum redus: economisesc bateria. Zzz..." }, { "Best performance: full power!", "Performanță maximă: putere totală!" },
            { "GAMING! Everything goes to the game.", "GAMING! Totul merge la joc." }, { "Work mode: quick apps, no notifications. Let's focus.", "Mod lucru: aplicații rapide, fără notificări. Concentrare." },
            { "Back to normal.", "Înapoi la normal." }, { "Approve the Windows prompt to go turbo...", "Aprobă fereastra Windows ca să trecem pe turbo..." },
            { "Leaving gaming mode", "Ies din gaming" }, { "Gaming mode: let's go!", "Gaming: hai!" }, { "Freeing memory", "Eliberez memoria" },
            { "Charging mode off. The phone still charges while the laptop is on: unplug it to stop.", "Modul de încărcare e oprit. Telefonul se încarcă în continuare cât laptopul e pornit: scoate-l din priză ca să se oprească." }, { "Keeping the USB ports powered for your phone", "Țin porturile USB alimentate pentru telefon" },
            { "I'm back!", "M-am întors!" }, { "Checking the PC...", "Verific PC-ul..." },
            { "Net focus off. Approve to put the network back.", "Net focus oprit. Aprobă ca să pun rețeaua la loc." },
            { "I can not manage that program's network.", "Nu pot gestiona rețeaua acelui program." },
            { "Net focus needs your approval; nothing changed.", "Net focus are nevoie de aprobarea ta; nu s-a schimbat nimic." },
            { "Pomodoro: 25 minutes of focus. I'll tell you when to take a break.", "Pomodoro: 25 de minute de concentrare. Îți spun când să iei pauză." },
            { "Break time! 5 minutes away from the screen.", "Pauză! 5 minute departe de ecran." },
            { "Back to work: another 25 minutes.", "Înapoi la lucru: încă 25 de minute." },
            { "Pomodoro off.", "Pomodoro oprit." },
            { "The PC is in great shape. I got a crown!", "PC-ul e în formă maximă. Am primit coroană!" },
            { "Looking for \"", "Caut \"" }, { "I could not start the search: ", "Nu am putut porni căutarea: " },
            { "Ready. Switch on what you want and press Apply now.", "Gata. Pornește ce vrei și apasă Aplică acum." },
            { "Pomodoro: focus, ", "Pomodoro: concentrare, " }, { "Pomodoro: break, ", "Pomodoro: pauză, " },
            { "The buddy wears a crown while the PC stays this healthy.", "Păianjenul poartă coroană cât PC-ul rămâne așa de sănătos." },
            { "Nearly there: see the rows below for the last few points.", "Aproape: rândurile de mai jos arată ultimele puncte." },
            { "The rows below say where the points went.", "Rândurile de mai jos arată unde s-au dus punctele." },
            { "Saved. Import this file on another PC and press My full setup there.", "Salvat. Importă fișierul pe alt PC și apasă acolo Setarea mea completă." },
            { "Imported. Restart TaskBoost to see the settings, then press Apply now on the Boost tab.", "Importat. Repornește TaskBoost ca să vezi setările, apoi apasă Aplică acum pe ecranul Boost." },
            { "Remove TaskBoost? Every setting it changed goes back, sign-in entries and the Claude link are removed, and its data folder is deleted. The TaskBoost folder itself stays for you to delete.\n\nContinue?", "Dezinstalezi TaskBoost? Toate setările schimbate revin, intrările de la logare și legătura cu Claude sunt scoase, iar folderul lui de date e șters. Folderul TaskBoost rămâne să-l ștergi tu.\n\nContinui?" },
            { "Removing TaskBoost...", "Dezinstalez TaskBoost..." },
            { "Administrator: approve once for this session", "Administrator: aprobă o dată pentru sesiunea asta" },
            { "Administrator: approved for this session", "Administrator: aprobat pentru sesiunea asta" },
            { "TaskBoost needs administrator approval for this.\n\nApprove once for this session, so it does not ask again until you exit TaskBoost?\n\n(No: Windows asks at every action, as before.)", "TaskBoost are nevoie de aprobare de administrator pentru asta.\n\nAprobi o dată pentru sesiunea asta, ca să nu mai întrebe până închizi TaskBoost?\n\n(Nu: Windows întreabă la fiecare acțiune, ca înainte.)" }, { "Pomodoro", "Pomodoro" },

            // dialogs
            { "Yes", "Da" }, { "No", "Nu" }, { "Search", "Caută" }, { "Cancel", "Renunță" }, { "Search in ", "Caută în " },
            { "Nothing to restore: TaskBoost has not changed anything on this PC yet.", "Nimic de readus: TaskBoost nu a schimbat încă nimic pe acest PC." },
            { "Put every setting back to the value it had before TaskBoost first changed it?", "Readuc fiecare setare la valoarea pe care o avea înainte ca TaskBoost să o schimbe prima dată?" },
            { "Windows will stop installing updates by itself, including security fixes.\n\nYou can still check and install by hand from here. Stop automatic updates?", "Windows nu va mai instala singur actualizări, nici pe cele de securitate.\n\nPoți în continuare să cauți și să instalezi manual de aici. Oprești actualizările automate?" },
            { "Close every running copy of this program?", "Închid toate copiile pornite ale acestui program?" }, { "Unsaved work in it will be lost.", "Ce nu ai salvat în el se pierde." },
            { "Could not close it: ", "Nu l-am putut închide: " }, { "Programs running as administrator can only be closed by an administrator.", "Programele pornite ca administrator pot fi închise doar de un administrator." },
            { "Could not connect to Claude: ", "Nu m-am putut conecta la Claude: " },
            { "Claude Code is needed to start Claude from here. Install it from claude.com/claude-code, then press Ask Claude again.", "E nevoie de Claude Code ca să pornești Claude de aici. Instalează-l de la claude.com/claude-code, apoi apasă din nou Întreabă Claude." },
            { "Select a row that belongs to a program first.", "Selectează întâi un rând care ține de un program." }, { "That program is no longer running.", "Programul acela nu mai rulează." },
            { "TaskBoost marks this one as needed:", "TaskBoost îl consideră necesar:" }, { "Switch it off anyway?", "Îl oprești totuși?" },
            { "Stop this background service from starting with Windows?", "Oprești pornirea acestui serviciu de fundal odată cu Windows?" },
            { "It keeps running until the next restart. You can switch it back on here.", "Rămâne pornit până la următoarea repornire. Îl poți porni la loc de aici." },
            { "It could not be switched:", "Nu a putut fi comutat:" },
            { "Select an item in the list first.", "Selectează întâi un element din listă." },
            { "This entry runs only once and then removes itself, so it has no on/off switch.", "Intrarea asta rulează o singură dată și apoi se șterge singură, deci nu are comutator." },
            { "Choose the game's program file", "Alege fișierul program al jocului" },
            { "Browsers and chat apps will be closed without asking them to save.\n\nContinue?", "Browserele și aplicațiile de chat vor fi închise fără să fie întrebate dacă salvează.\n\nContinui?" },
            { "The Recycle Bin will be emptied for good.\n\nContinue?", "Coșul de reciclare va fi golit definitiv.\n\nContinui?" },

            // Guard and Startup lists
            { "All quiet", "Liniște" }, { "Nothing to report from the checks that are switched on", "Nimic de raportat de la verificările pornite" },
            { "Keep", "Păstrează" }, { "Useful", "Util" }, { "Optional", "Opțional" }, { "Check", "Verifică" }, { "Leftover", "Rămășiță" }, { "Suspicious", "Suspect" }, { "Off", "Oprit" },
            { "The scan failed: ", "Scanarea a eșuat: " }, { " items:  ", " elemente:  " }, { " suspicious,  ", " suspecte,  " }, { " to check,  ", " de verificat,  " }, { " switched off,  ", " oprite,  " },
            { " fine.  Select one and use the switch button below.", " în regulă.  Selectează unul și folosește butonul de mai jos." },

            // Home: live readings and what the score is made of
            { "Animations: on", "Animații: pornite" }, { "Animations: off", "Animații: oprite" },
            { "Disk", "Disc" }, { "Battery", "Baterie" }, { "Running for", "Pornit de" },
            { " free of ", " liberi din " }, { "No battery: runs on mains", "Fără baterie: merge de la priză" },
            { ", charging", ", se încarcă" }, { ", plugged in", ", la priză" }, { ", on battery", ", pe baterie" }, { " days ", " zile " }, { ": a restart would help", ": o repornire ar ajuta" },
            { "Nothing suspicious on the Guard tab", "Nimic suspect pe ecranul Gardă" }, { " alert(s) and ", " alerte și " }, { " notice(s) on the Guard tab", " atenționări pe ecranul Gardă" },
            { "Everything that starts with Windows looks fine", "Tot ce pornește cu Windows arată bine" }, { " suspicious and ", " suspecte și " }, { " to check on the Startup tab", " de verificat pe ecranul Pornire" },
            { " has ", " are " }, { "% free", "% liber" }, { ": free some space", ": eliberează spațiu" }, { " MB of ", " MB din " }, { " MB free right now", " MB liberi acum" },
            { "Windows Defender real-time protection is off", "Protecția în timp real Windows Defender e oprită" }, { "Windows Firewall is off", "Windows Firewall e oprit" }, { "Defender and the firewall are on", "Defender și firewall-ul sunt pornite" },
            { " MB of old temporary files (Turbo tab, Clean now)", " MB de fișiere temporare vechi (ecranul Turbo, Curăță acum)" },
            { "Automatic Windows updates are stopped (Care tab)", "Actualizările automate Windows sunt oprite (ecranul Îngrijire)" }, { "Windows updates itself", "Windows se actualizează singur" },

            // net focus
            { "Net focus on ", "Net focus pe " }, { ". Approve the Windows prompt...", ". Aprobă fereastra Windows..." }, { "Net focus did not start: ", "Net focus nu a pornit: " },
            { " goes first on the internet. Other programs held back: ", " e primul la internet. Alte programe ținute pe loc: " }, { "That did not work: ", "Nu a mers: " }
        };
    }
}
