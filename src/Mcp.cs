using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TaskBoost
{
    // Sets up and starts Claude from inside the app, so nothing has to be typed in a terminal
    static class ClaudeLink
    {
        const string StartPrompt = "You are connected to my PC through the taskboost tools. Greet me through the buddy, then run guard_scan, startup_items and health_report and tell me in plain words whether anything needs my attention.";

        public static bool ClaudeCodeInstalled()
        {
            string output;
            return Util.Run("cmd.exe", "/c where claude", out output) == 0;
        }

        // Registers this exe as the "taskboost" MCP server wherever Claude is installed. Safe to call on every start:
        // it does nothing when the entry already points at this exe, and repairs it when the folder was moved.
        public static string EnsureRegistered()
        {
            string escaped = App.ExePath.Replace("\\", "\\\\"), result = "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (ClaudeCodeInstalled())
            {
                string config = Path.Combine(home, ".claude.json");
                string text = File.Exists(config) ? File.ReadAllText(config) : "";
                if (text.Contains("\"taskboost\"") && text.Contains(escaped)) result = "Claude Code: already connected";
                else
                {
                    string output;
                    Util.Run("cmd.exe", "/c claude mcp remove taskboost --scope user", out output);
                    result = Util.Run("cmd.exe", "/c claude mcp add --scope user taskboost -- \"" + App.ExePath + "\" --mcp", out output) == 0
                        ? "Claude Code: connected" : "Claude Code: could not connect (" + output + ")";
                }
            }

            // Claude Desktop keeps its servers in a JSON file
            string desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            if (Directory.Exists(desktopDir))
            {
                try
                {
                    string file = Path.Combine(desktopDir, "claude_desktop_config.json");
                    var json = new JavaScriptSerializer();
                    var root = File.Exists(file) ? json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)) : null;
                    root = root ?? new Dictionary<string, object>();
                    var servers = root.ContainsKey("mcpServers") ? root["mcpServers"] as Dictionary<string, object> : null;
                    servers = servers ?? new Dictionary<string, object>();
                    var mine = servers.ContainsKey("taskboost") ? servers["taskboost"] as Dictionary<string, object> : null;
                    if (mine == null || Convert.ToString(mine.ContainsKey("command") ? mine["command"] : "") != App.ExePath)
                    {
                        servers["taskboost"] = new Dictionary<string, object> { { "command", App.ExePath }, { "args", new[] { "--mcp" } } };
                        root["mcpServers"] = servers;
                        if (File.Exists(file)) File.Copy(file, file + ".taskboost-backup", true);
                        File.WriteAllText(file, json.Serialize(root));
                        result += (result.Length > 0 ? "; " : "") + "Claude Desktop: connected (restart Claude Desktop)";
                    }
                    else result += (result.Length > 0 ? "; " : "") + "Claude Desktop: already connected";
                }
                catch (Exception ex) { result += (result.Length > 0 ? "; " : "") + "Claude Desktop: could not connect (" + ex.Message + ")"; }
            }
            return result.Length > 0 ? result : "Claude is not installed on this PC";
        }

        // Opens Claude Code in its own window, already asked to look over the PC
        public static void Launch()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/k claude \"" + StartPrompt + "\"");
            psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Process.Start(psi);
        }
    }

    // "TaskBoost.exe --mcp": a Model Context Protocol server over stdin/stdout, so Claude can look at this PC
    // through TaskBoost and speak through the desktop buddy. It only reads and reports, apart from
    // freeing memory and switching gaming mode, and it never deletes anything.
    static class Mcp
    {
        class Tool
        {
            public string Name, Description;
            public Dictionary<string, object> Properties = new Dictionary<string, object>();
            public string[] Required = new string[0];
            public Func<Dictionary<string, object>, string> Run;
        }

        const string Instructions =
            "TaskBoost is a Windows performance and security tool running on the user's PC. While you are connected you appear on " +
            "their screen as a small blue pixel spider, the desktop buddy. Use buddy_say for short, friendly reactions (one sentence) " +
            "with a fitting mood; do not narrate every step. The other tools read the PC's state. Nothing here deletes files.";

        static readonly string[] Moods = BuddySprite.Moods;

        static List<Tool> Tools()
        {
            var tools = new List<Tool>();
            tools.Add(new Tool
            {
                Name = "guard_scan",
                Description = "Security snapshot of this PC right now: apps using the camera, microphone or location, programs with open internet connections and who signed them, connected keyboards, remote-access tools and Remote Desktop, new startup programs, suspicious processes, proxy, hosts file, Defender and firewall state. Each line starts with ALERT, NOTICE or INFO.",
                Run = delegate
                {
                    var sb = new StringBuilder();
                    foreach (var f in Guard.Scan(new[] { "sensors", "network", "keyboards", "remote", "startup", "shield" }))
                        sb.AppendLine((f.Level == 2 ? "ALERT  " : f.Level == 1 ? "NOTICE " : "INFO   ") + f.Title + " | " + f.Detail);
                    return sb.Length == 0 ? "All quiet: nothing to report." : sb.ToString();
                }
            });
            tools.Add(new Tool
            {
                Name = "startup_items",
                Description = "Everything that starts with Windows on this PC (sign-in entries, Startup folders, non-Windows background services), with what each one does, who signed it, and a verdict: Keep, Useful, Optional, OK, Check, Leftover, Suspicious or Off.",
                Run = delegate
                {
                    var sb = new StringBuilder();
                    foreach (var f in StartupInfo.Scan()) sb.AppendLine("[" + f.Tag + "] " + f.Title + " | " + f.Detail + " | " + f.Extra);
                    return sb.ToString();
                }
            });
            tools.Add(new Tool
            {
                Name = "health_report",
                Description = "Hardware and system health: time since last restart, pending restart, free memory, free space per drive, disk health (SMART), battery wear and charge cycles.",
                Run = delegate { return Capture(delegate { Care.Run(new[] { "health" }); }); }
            });
            tools.Add(new Tool
            {
                Name = "cleanup_preview",
                Description = "Shows how much space a safe clean-up would free (temp folders, crash reports, Recycle Bin) without deleting anything.",
                Run = delegate { return Capture(delegate { Cleaner.Run(new[] { "temp", "wintemp", "reports", "recycle" }, true); }); }
            });
            tools.Add(new Tool
            {
                Name = "free_memory",
                Description = "Asks running programs to give back memory they are not using. Nothing is closed and no data is lost. Returns free memory before and after.",
                Run = delegate { return Capture(Memory.Free); }
            });
            var gaming = new Tool
            {
                Name = "gaming_mode",
                Description = "Switches TaskBoost's temporary gaming mode on or off (High performance power plan, paused update/indexing services, freed memory). Windows asks the user for administrator approval. Switching it off restores everything.",
                Required = new[] { "on" },
                Run = delegate(Dictionary<string, object> a) { return Helper.Run(Convert.ToBoolean(a["on"]) ? "game-on --options apps,services" : "game-off"); }
            };
            gaming.Properties["on"] = Schema("boolean", "true to enter gaming mode, false to leave it");
            tools.Add(gaming);

            var say = new Tool
            {
                Name = "buddy_say",
                Description = "Makes the desktop buddy (the blue pixel spider that is you on the user's screen) show a speech bubble and a mood. Keep the text to one short sentence.",
                Required = new[] { "text" },
                Run = delegate(Dictionary<string, object> a)
                {
                    string mood = a.ContainsKey("mood") ? Convert.ToString(a["mood"]) : "happy";
                    if (Array.IndexOf(Moods, mood) < 0) mood = "happy";
                    Inbox("say|" + mood + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToString(a["text"]))));
                    return "The buddy said it.";
                }
            };
            say.Properties["text"] = Schema("string", "What the buddy says, one short sentence");
            var moodSchema = Schema("string", "The buddy's expression");
            moodSchema["enum"] = Moods;
            say.Properties["mood"] = moodSchema;
            tools.Add(say);

            var visible = new Tool
            {
                Name = "buddy_visible",
                Description = "Calls the desktop buddy onto the screen, or sends it away until it is called back.",
                Required = new[] { "visible" },
                Run = delegate(Dictionary<string, object> a)
                {
                    bool show = Convert.ToBoolean(a["visible"]);
                    Inbox(show ? "show" : "hide");
                    return show ? "The buddy is coming back." : "The buddy left the screen.";
                }
            };
            visible.Properties["visible"] = Schema("boolean", "true to show the buddy, false to send it away");
            tools.Add(visible);
            return tools;
        }

        static Dictionary<string, object> Schema(string type, string description)
        {
            return new Dictionary<string, object> { { "type", type }, { "description", description } };
        }

        static void Inbox(string line)
        {
            File.AppendAllText(Path.Combine(App.DataDir, BuddyForm.InboxFile), line + Environment.NewLine);
        }

        // Runs something that reports through the log and returns those lines without their timestamps
        public static string Capture(Action work)
        {
            var sb = new StringBuilder();
            Action<string> sink = delegate(string line) { sb.AppendLine(line.Length > 21 ? line.Substring(21) : line); };
            Log.Line += sink;
            try { work(); }
            finally { Log.Line -= sink; }
            return sb.ToString();
        }

        public static int Run()
        {
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.NewLine = "\n";
            output.AutoFlush = true;
            var json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
            var tools = Tools();

            // The buddy lives in the normal TaskBoost window process: start it in the tray if needed, and keep
            // touching a file so it knows Claude is connected
            string alive = Path.Combine(App.DataDir, BuddyForm.AliveFile);
            using (var heartbeat = new Timer(delegate { try { File.WriteAllText(alive, DateTime.Now.ToString("s")); } catch (IOException) { } }, null, 0, 4000))
            {
                try { Process.Start(App.ExePath, "--guard"); }
                catch (Exception) { }

                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0) continue;
                    Dictionary<string, object> reply = null;
                    try { reply = Handle(json.Deserialize<Dictionary<string, object>>(line), tools); }
                    catch (Exception ex)
                    {
                        reply = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", null }, { "error", new Dictionary<string, object> { { "code", -32700 }, { "message", ex.Message } } } };
                    }
                    if (reply != null) output.WriteLine(json.Serialize(reply));
                }
            }
            try { File.Delete(alive); }
            catch (IOException) { }
            return 0;
        }

        // One JSON-RPC message in, one reply out (null for notifications)
        static Dictionary<string, object> Handle(Dictionary<string, object> message, List<Tool> tools)
        {
            object id;
            bool wantsReply = message.TryGetValue("id", out id);
            string method = message.ContainsKey("method") ? message["method"] as string : null;
            if (method == null || !wantsReply) return null;
            var parameters = message.ContainsKey("params") ? message["params"] as Dictionary<string, object> : null;
            parameters = parameters ?? new Dictionary<string, object>();
            object result;

            if (method == "initialize")
            {
                result = new Dictionary<string, object>
                {
                    { "protocolVersion", parameters.ContainsKey("protocolVersion") ? parameters["protocolVersion"] : "2025-06-18" },
                    { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                    { "serverInfo", new Dictionary<string, object> { { "name", "taskboost" }, { "version", "1.1.2" } } },
                    { "instructions", Instructions }
                };
            }
            else if (method == "ping") result = new Dictionary<string, object>();
            else if (method == "tools/list")
            {
                var list = new List<object>();
                foreach (var t in tools)
                    list.Add(new Dictionary<string, object>
                    {
                        { "name", t.Name }, { "description", t.Description },
                        { "inputSchema", new Dictionary<string, object> { { "type", "object" }, { "properties", t.Properties }, { "required", t.Required } } }
                    });
                result = new Dictionary<string, object> { { "tools", list } };
            }
            else if (method == "tools/call")
            {
                string name = parameters.ContainsKey("name") ? parameters["name"] as string : null;
                var arguments = parameters.ContainsKey("arguments") ? parameters["arguments"] as Dictionary<string, object> : null;
                Tool tool = tools.Find(delegate(Tool t) { return t.Name == name; });
                string text;
                bool failed = false;
                if (tool == null) { text = "Unknown tool: " + name; failed = true; }
                else
                {
                    try { text = tool.Run(arguments ?? new Dictionary<string, object>()); }
                    catch (Exception ex) { text = "The tool failed: " + ex.Message; failed = true; }
                }
                result = new Dictionary<string, object>
                {
                    { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                    { "isError", failed }
                };
            }
            else
            {
                return new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "error", new Dictionary<string, object> { { "code", -32601 }, { "message", "Method not found: " + method } } } };
            }
            return new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } };
        }
    }
}
