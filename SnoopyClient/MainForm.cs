using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SnoopyClient
{

    public partial class MainForm : Form
    {

        private UserInterface ui = null;
        private Configuration cfg = null;
        private Engine eng = null;
        private string clientName = "SnoopyClient";
        private string configPath = "";

        public MainForm()
        {
            InitializeComponent();
            FormClosing += MainForm_FormClosing;
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker");
            configPath = Path.Combine(configPath, "Config");
            cfg = LoadConfigFromFile(Path.Combine(configPath, clientName + ".config.xml"));
            eng = new Engine();
            eng.cfg = cfg;
            userInterface1.eng = eng;
            if (cfg.AutoStart == true)
            {
                eng.Start();
            }

        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfigToFile(cfg, Path.Combine(configPath, clientName + ".config.xml"), true);
            eng.Stop();
        }

        private Configuration LoadConfigFromFile(string filename)
        {
            try
            {
                FileInfo fi = new FileInfo(filename);
                string origfilename = filename;
                string cre = "";
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
