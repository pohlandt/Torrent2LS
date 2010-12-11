using System;
using System.IO;
using System.Windows.Forms;
using System.Net;
using System.Threading;

namespace Torrent2LS
{
    static class Program
    {

        private static NotifyIcon _notifyIcon = new NotifyIcon(new ControlContainer());
        private static ApplicationContext _context = new ApplicationContext();
        private static string[] _args;
        private const int NOTIFY_DURATION = 5000;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Log("## Torrent2LS start ##");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            _notifyIcon.Text = "Torrent2LS";
            _notifyIcon.Visible = true;
            _notifyIcon.Icon = new System.Drawing.Icon(Application.StartupPath + @"\torrent.ico");

            _args = args;
            
            foreach (string arg in args)
                Log("arg: " + arg);
         
            Thread t = new Thread(new ThreadStart(Run));
            t.Start();

            Application.Run(_context);
            Log("## Torrent2LS end ##");
        }

        static void Run()
        {
            if (_args.Length == 0)
            {
                string message = "Argument 1 must be a valid torrent.";
                Log(message);
                Notify(message, ToolTipIcon.Error);
                Exit(1);
                return;
            }

            string torrentFile = _args[0];
            FileInfo torrentFileInfo = new FileInfo(torrentFile);
            if (!torrentFileInfo.Exists)
            {
                string message = "Argument is not a valid file:\n" + torrentFile;
                Log(message);
                Notify(message, ToolTipIcon.Error);
                Exit(2);
                return;
            }

            IPAddress lsIp = null;
            string configPath = Application.StartupPath + @"\lsip.txt";
            Log("using config path " + configPath);

            try
            {
                string ip = null;
                if (_args.Length == 2) ip = _args[1];
                else
                {
                    using(StreamReader reader = new StreamReader(configPath, System.Text.Encoding.Unicode))
                        ip = reader.ReadToEnd();
                }

                Log("got ip: " + ip);

                lsIp = IPAddress.Parse(ip);
            }
            catch (Exception ex)
            {
                string message = "Error while getting LinkStation IP from config file: " + configPath + "\n"
                    + ex.ToString();
                Log(message);
                Notify(message, ToolTipIcon.Error);
                Exit(3);
                return;
            }

            Exit(PostTorrent(torrentFileInfo, lsIp));
            
            
        }

        private static void Log(string message)
        {
#if LOGGING
            try
            {
                using (StreamWriter writer = new StreamWriter(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Torrent2LS.log.txt", true))
                {
                    writer.WriteLine(string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), message));
                }
            }
            catch (Exception) { }
#endif
        }

        private static void Exit(int code){
            Environment.ExitCode = code;
            _notifyIcon.Visible = false;
            Application.DoEvents();
            _context.ExitThread();
        }

        private static void Notify(string message, ToolTipIcon icon)
        {
            _notifyIcon.ShowBalloonTip(NOTIFY_DURATION, "Torrent2LS", message, icon);
            Application.DoEvents();
            Thread.Sleep(NOTIFY_DURATION);
        }

        private static byte PostTorrent(FileInfo torrent, IPAddress lsIP)
        {
            try
            {
                Log(string.Format("Sending file {0} to {1}", torrent.Name, lsIP.ToString()));

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    string.Format("http://{0}:8080/api/torrent-add?start=yes", lsIP.ToString()));

                request.Method = "POST";
                string boundary = "----------------------------" + DateTime.Now.Ticks.ToString("x");
                request.ContentType = "multipart/form-data; boundary=" + boundary;
                request.KeepAlive = true;
                request.Credentials = CredentialCache.DefaultCredentials;

                using (BinaryWriter writer = new BinaryWriter(request.GetRequestStream()))
                {
                    byte[] boundarybytes = System.Text.Encoding.ASCII.GetBytes("\r\n--" + boundary + "\r\n");
                    writer.Write(boundarybytes);

                    string headerTemplate = "Content-Disposition: form-data; name=\"fileEl\";filename=\"{0}\"\r\n Content-Type: application/x-bittorrent\r\n\r\n";
                    string header = string.Format(headerTemplate, torrent.Name);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes(header));

                    using (BinaryReader reader = new BinaryReader(torrent.OpenRead()))
                        writer.Write(reader.ReadBytes((int)torrent.Length));

                    writer.Write(boundarybytes);
                }

                using (StreamReader reader = new StreamReader(request.GetResponse().GetResponseStream()))
                {
                    string response = reader.ReadToEnd();
                    Log("response received: " + response);
                    
                    if (!response.Contains("Fail"))
                    {
                        var message = "Torrent added successfully: " + torrent.Name;
                        Log(message);
                        Notify(message, ToolTipIcon.Info);
                    }
                    else
                    {
                        var message = "LinkStation did not accept torrent: " + torrent.Name;
                        Log(message);
                        Notify(message, ToolTipIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                string message = "Error while posting to LinkStation:\n" + ex.ToString();
                Log(message);
                Notify(message, ToolTipIcon.Error);
                Exit(4);
            }

            return 0;
        }
    }
}
