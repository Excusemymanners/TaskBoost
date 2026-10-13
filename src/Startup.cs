using System.IO;
using System.Security;
using System.Text;

namespace TaskBoost
{
    // "Run at sign-in" is a scheduled task, so the administrator parts can run without a prompt at every sign-in
    static class Startup
    {
        const string TaskName = App.Name;

        public static bool IsEnabled()
        {
            string output;
            return Util.Run("schtasks.exe", "/query /tn \"" + TaskName + "\"", out output) == 0;
        }

        public static bool Install(string user)
        {
            string u = SecurityElement.Escape(user);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Applies the selected TaskBoost settings at sign-in.</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + u + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + u + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(App.ExePath) + "</Command><Arguments>--apply</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string file = Path.Combine(App.DataDir, "startup-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            bool ok = Util.RunLogged("schtasks.exe", "/create /tn \"" + TaskName + "\" /xml \"" + file + "\" /f");
            if (ok) Log.Info("TaskBoost will now run at sign-in for " + user);
            return ok;
        }

        public static bool Remove()
        {
            bool ok = Util.RunLogged("schtasks.exe", "/delete /tn \"" + TaskName + "\" /f");
            if (ok) Log.Info("TaskBoost no longer runs at sign-in");
            return ok;
        }
    }
}
