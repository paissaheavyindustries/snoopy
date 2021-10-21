using System;
using System.Xml.Serialization;

namespace SnoopyClient
{

    public class Configuration
    {

        internal Engine.EngineModeEnum _OperationMode { get; set; } = Engine.EngineModeEnum.Client;
        [XmlAttribute]
        public string OperationMode
        {
            get
            {
                return _OperationMode.ToString();
            }
            set
            {
                _OperationMode = (Engine.EngineModeEnum)Enum.Parse(typeof(Engine.EngineModeEnum), value);
            }
        }

        [XmlAttribute]
        public string Host { get; set; } = "localhost";

        [XmlAttribute]
        public int Port { get; set; } = 46761;

        [XmlAttribute]
        public bool AutoStart { get; set; } = false;

        [XmlAttribute]
        public bool DumpParsed { get; set; } = false;

        [XmlAttribute]
        public bool DumpNetwork { get; set; } = false;

        [XmlAttribute]
        public string DumpFolderParsed { get; set; } = "";

        [XmlAttribute]
        public string DumpFolderNetwork { get; set; } = "";

        [XmlAttribute]
        public string DumpDateFormat { get; set; } = "yyyyMMdd_hhmmss";

    }

}
