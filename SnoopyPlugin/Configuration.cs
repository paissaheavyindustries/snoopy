using System;
using System.Xml.Serialization;

namespace SnoopyPlugin
{

    public class Configuration
    {

        internal Engine.EngineModeEnum _OperationMode { get; set; } = Engine.EngineModeEnum.Server;
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
        public bool NetworkTimes { get; set; } = true;

        [XmlAttribute]
        public bool AutoStart { get; set; } = false;

        [XmlAttribute]
        public bool QueueDisconnected { get; set; } = false;

    }

}
