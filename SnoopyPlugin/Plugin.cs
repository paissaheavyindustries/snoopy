using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Serialization;
using Advanced_Combat_Tracker;

namespace SnoopyPlugin
{

    public class Plugin : IActPluginV1
    {

        private UserInterface ui = null;
        private Configuration cfg = null;
        private Engine eng = null;
        private string pluginName = "SnoopyPlugin";
        private string configPath = "";

        private void SubscribeToNetworkEvents()
        {
            object plug = null;
            foreach (ActPluginData p in ActGlobals.oFormActMain.ActPlugins)
            {
                string tn = p.pluginObj != null ? p.pluginObj.GetType().Name : "(null)";
                if (
                    (
                        (String.Compare(p.pluginFile.Name, "FFXIV_ACT_Plugin.dll", true) == 0)
                        ||
                        (String.Compare(tn, "FFXIV_ACT_Plugin", true) == 0)
                    )
                    &&
                    (
                        (String.Compare(p.lblPluginStatus.Text, "FFXIV Plugin Started.", true) == 0)
                        ||
                        (String.Compare(p.lblPluginStatus.Text, "FFXIV_ACT_Plugin Started.", true) == 0)
                    )
                )
                {
                    plug = p.pluginObj;
                }
            }
            if (plug == null)
            {
                throw new ArgumentException("Can't find FFXIV parsing plugin");
            }
            PropertyInfo pi = plug.GetType().GetProperty("DataSubscription", BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi == null)
            {
                throw new ArgumentException("No DataSubscription found on FFXIV parsing plugin");
            }
            dynamic subs = pi.GetValue(plug);
            if (subs == null)
            {
                throw new ArgumentException("DataSubscription not initialized on FFXIV parsing plugin");
            }
            EventInfo ei = subs.GetType().GetEvent("ParsedLogLine", BindingFlags.GetField | BindingFlags.Public | BindingFlags.Instance);
            if (subs == null)
            {
                throw new ArgumentException("No ParsedLogLine found on FFXIV parsing plugin");
            }
            MethodInfo mix = this.GetType().GetMethod("NetworkLogLineReceiver");
            Type deltype = ei.EventHandlerType;
            Delegate handler = Delegate.CreateDelegate(deltype, this, mix);
            ei.AddEventHandler(subs, handler);
        }

        public void InitPlugin(TabPage tp, Label l)
        {
            try
            {
                l.Text = "Initializing";
                tp.Text = "Snoopy";
                pluginName = Path.GetFileNameWithoutExtension(pluginName);
                configPath = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config");
                cfg = LoadConfigFromFile(Path.Combine(configPath, pluginName + ".config.xml"));
                eng = new Engine();
                eng.cfg = cfg;
                ui = new UserInterface();
                ui.eng = eng;
                tp.Controls.Add(ui);
                ui.Dock = DockStyle.Fill;
                ActGlobals.oFormActMain.OnLogLineRead += OFormActMain_OnLogLineRead;
                SubscribeToNetworkEvents();
                l.Text = "Ready";
                if (cfg.AutoStart == true)
                {
                    eng.Start();
                }
            }
            catch (Exception ex)
            {
                l.Text = "Exception: " + ex.Message + " @ " + ex.StackTrace;
            }
        }

        private void OFormActMain_OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            try
            {
                if (isImport == true)
                {
                    return;
                }
                Engine.GameEvent ge = new Engine.GameEvent() { Timestamp = logInfo.detectedTime.ToUniversalTime(), IsNetwork = false, EventText = logInfo.logLine, EventZone = logInfo.detectedZone };
                eng.QueueGameEvent(ge);
            }
            catch (Exception)
            {
            }
        }

        public void NetworkLogLineReceiver(uint sequence, int messagetype, string message)
        {
            try
            {
                DateTime dt = DateTime.Now;
                string preamble;
                if (cfg.NetworkTimes == true)
                {
                    DateTime ldt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
                    preamble = String.Format("{0:00}|{1:O}|", messagetype, ldt);
                }
                else
                {
                    preamble = String.Format("{0:00}|{1}|", messagetype, sequence.ToString());
                }
                string linex = preamble + message;
                Engine.GameEvent ge = new Engine.GameEvent() { Timestamp = dt.ToUniversalTime(), IsNetwork = true, EventText = linex, EventZone = "" };
                eng.QueueGameEvent(ge);
            }
            catch (Exception)
            {
            }
        }

        public void DeInitPlugin()
        {
            SaveConfigToFile(cfg, Path.Combine(configPath, pluginName + ".config.xml"), true);
            if (ui != null)
            {
                ui.ShuttingDown = true;
                ui.eng = null;
                ui.Dispose();
                ui = null;
            }
            if (eng != null)
            {
                eng.Stop();
                eng.Dispose();
                eng = null;
            }
        }

        private Configuration LoadConfigFromFile(string filename)
        {
            try
            {
                FileInfo fi = new FileInfo(filename);
                string origfilename = filename;
                if (fi.Exists == false)
                {
                    return new Configuration();
                }
                Configuration cx = null;
                XmlSerializer xs = new XmlSerializer(typeof(Configuration));
                using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read))
                {
                    cx = (Configuration)xs.Deserialize(fs);
                }
                return cx;
            }
            catch (Exception ex)
            {
            }
            return new Configuration();
        }

        private void SaveConfigToFile(Configuration cfg, string filename, bool switchprevious)
        {
            try
            {
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                string test = "";
                ns.Add("", "");
                XmlSerializer xs = new XmlSerializer(typeof(Configuration));
                using (MemoryStream ms = new MemoryStream())
                {
                    xs.Serialize(ms, cfg, ns);
                    ms.Position = 0;
                    using (StreamReader sr = new StreamReader(ms))
                    {
                        test = sr.ReadToEnd();
                    }
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    using (StreamWriter sw = new StreamWriter(ms))
                    {
                        sw.Write(test);
                        sw.Flush();
                        ms.Position = 0;
                        Configuration cx = (Configuration)xs.Deserialize(ms);
                    }
                }
                using (FileStream fs = File.Open(filename + ".temp", FileMode.Create, FileAccess.Write))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(test);
                        sw.Flush();
                    }
                }
                File.Copy(filename + ".temp", filename, true);
                File.Delete(filename + ".temp");
            }
            catch (Exception ex)
            {
            }
        }

    }

}
