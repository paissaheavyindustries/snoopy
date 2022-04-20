using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SnoopyClient
{

    class Engine : IDisposable
    {

        internal enum LogEventTypeEnum
        {
            Error,
            Warning,
            Info,
            Debug
        }

        internal class LogEvent
        {

            public DateTime Timestamp { get; set; }
            public LogEventTypeEnum Type { get; set; }
            public string Description { get; set; }

        }

        internal class GameEvent
        {

            public DateTime Timestamp { get; set; }
            public bool IsNetwork { get; set; }
            public string EventText { get; set; }
            public string EventZone { get; set; }

        }

        internal enum NetworkStateEnum
        {
            Disconnected,
            Connecting,
            Connected,
            Disconnecting,
            Listening
        }

        public int StatsEventsParsedSeen { get; set; } = 0;
        public int StatsEventsNetworkSeen { get; set; } = 0;
        public int StatsEventsInQueue { get; set; } = 0;
        public int StatsReceivesTriggered { get; set; } = 0;
        public int StatsBytesReceived { get; set; } = 0;
        public DateTime StatsLatestLogEvent { get; set; } = DateTime.MinValue;
        public NetworkStateEnum StatsNetworkState { get; set; } = NetworkStateEnum.Disconnected;

        internal Configuration cfg { get; set; }

        private Socket listenSocket = null;
        private Socket connSocket = null;
        private Socket remoteSocket = null;
        private object socketLock = new object();
        internal List<LogEvent> log = new List<LogEvent>();
        private List<GameEvent> recvParsedQueue = new List<GameEvent>();
        private List<GameEvent> recvNetworkQueue = new List<GameEvent>();
        private byte[] recvBuffer = new byte[8096];
        private string recvStream = "";
        private Thread EngineThread = null;
        private string dumpFileName = "";
        private Regex rexCapture = new Regex(@"\x02(?<MessageData>[^\x03]*?)\x03");
        private ManualResetEvent ExitEvent = new ManualResetEvent(false);
        internal delegate void StateChangeDelegate(EngineStateEnum oldstate, EngineStateEnum newstate);
        internal event StateChangeDelegate OnStateChange;

        private EngineStateEnum __State = EngineStateEnum.Stopped;

        private EngineStateEnum _State
        {
            get
            {
                return __State;
            }
            set
            {
                if (__State != value)
                {
                    OnStateChange?.Invoke(__State, value);
                    __State = value;
                }
            }
        }

        internal EngineStateEnum State
        {
            get
            {
                return _State;
            }
        }

        public enum EngineModeEnum
        {
            Server,
            Client
        }

        public enum EngineStateEnum
        {
            Undefined,
            Stopped,
            Starting,
            Started,
            Stopping
        }

        public void Dispose()
        {
            if (ExitEvent != null)
            {
                ExitEvent.Dispose();
                ExitEvent = null;
            }
        }

        private void ResetStats()
        {
            StatsEventsParsedSeen = 0;
            StatsEventsNetworkSeen = 0;
            StatsEventsInQueue = 0;
            StatsReceivesTriggered = 0;
            StatsBytesReceived = 0;
            StatsNetworkState = NetworkStateEnum.Disconnected;
        }

        public void QueueGameEvent(GameEvent ge)
        {
            lock (recvParsedQueue)
            {
                if (ge.IsNetwork == false)
                {
                    recvParsedQueue.Add(ge);
                    StatsEventsParsedSeen++;
                }
                else
                {
                    recvNetworkQueue.Add(ge);
                    StatsEventsNetworkSeen++;
                }
                StatsEventsInQueue++;
            }
        }

        public void QueueLogEvent(LogEvent le)
        {
            lock (log)
            {
                log.Add(le);
                if (log.Count > 1000)
                {
                    log.RemoveAt(0);
                }
                StatsLatestLogEvent = DateTime.Now;
            }
        }

        public void Log(LogEventTypeEnum type, string desc)
        {
            QueueLogEvent(new LogEvent() { Timestamp = DateTime.Now, Type = type, Description = desc });
        }

        public bool IsConnected()
        {
            return (remoteSocket != null);
        }

        public void Start()
        {
            try
            {
                Log(LogEventTypeEnum.Info, "Engine starting");
                dumpFileName = DateTime.Now.ToString(cfg.DumpDateFormat);
                lock (this)
                {
                    if (_State == EngineStateEnum.Started || _State == EngineStateEnum.Starting)
                    {
                        Log(LogEventTypeEnum.Warning, "Engine already starting");
                        return;
                    }
                    _State = EngineStateEnum.Starting;
                }
                Log(LogEventTypeEnum.Debug, "Creating engine thread");
                EngineThread = new Thread(new ThreadStart(ThreadProc));
                EngineThread.Name = "SnoopyEngineThread";
                lock (recvParsedQueue)
                {
                    recvParsedQueue.Clear();
                    recvNetworkQueue.Clear();
                    ResetStats();
                }
                ExitEvent.Reset();
                Log(LogEventTypeEnum.Debug, "Starting engine thread");
                EngineThread.Start();
                return;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on Start: " + ex.Message + " @ " + ex.StackTrace);
            }
            Stop();
        }

        public void Stop()
        {
            try
            {
                Log(LogEventTypeEnum.Info, "Engine stopping");
                lock (this)
                {
                    if (_State == EngineStateEnum.Stopped || _State == EngineStateEnum.Stopping)
                    {
                        Log(LogEventTypeEnum.Warning, "Engine already stopped");
                        return;
                    }
                    _State = EngineStateEnum.Stopping;
                }
                Log(LogEventTypeEnum.Debug, "Stopping networking");
                StopNetworking();
                ExitEvent.Set();
                Log(LogEventTypeEnum.Debug, "Waiting for engine thread to stop");
                EngineThread.Join();
                Log(LogEventTypeEnum.Debug, "Engine stopped");
                _State = EngineStateEnum.Stopped;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on Stop: " + ex.Message + " @ " + ex.StackTrace);
            }
        }

        public static string Base64Decode(string str)
        {
            byte[] bs = System.Convert.FromBase64String(str);
            return System.Text.Encoding.UTF8.GetString(bs);
        }

        public void StartNetworking()
        {
            try
            {
                switch (cfg._OperationMode)
                {
                    case EngineModeEnum.Server:
                        StartListening();
                        break;
                    case EngineModeEnum.Client:
                        StartConnecting();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on StartNetworking: " + ex.Message + " @ " + ex.StackTrace);
                StopNetworking();
            }
        }

        public void StopNetworking()
        {
            Log(LogEventTypeEnum.Debug, "Networking stopping");
            StatsNetworkState = NetworkStateEnum.Disconnecting;
            lock (socketLock)
            {
                try
                {
                    if (connSocket != null)
                    {
                        connSocket.Shutdown(SocketShutdown.Both);
                        connSocket.Close();
                    }
                }
                catch (Exception ex)
                {
                    Log(LogEventTypeEnum.Error, "Exception on StopNetworking: " + ex.Message + " @ " + ex.StackTrace);
                }
                connSocket = null;
                try
                {
                    if (remoteSocket != null)
                    {
                        remoteSocket.Shutdown(SocketShutdown.Both);
                        remoteSocket.Close();
                    }
                }
                catch (Exception ex)
                {
                    Log(LogEventTypeEnum.Error, "Exception on StopNetworking: " + ex.Message + " @ " + ex.StackTrace);
                }
                remoteSocket = null;
                try
                {
                    if (listenSocket != null)
                    {
                        listenSocket.Close();
                    }
                }
                catch (Exception ex)
                {
                    Log(LogEventTypeEnum.Error, "Exception on StopNetworking: " + ex.Message + " @ " + ex.StackTrace);
                }
                listenSocket = null;
            }
            StatsNetworkState = NetworkStateEnum.Disconnected;
            Log(LogEventTypeEnum.Debug, "Networking stopped");
        }

        public void ThreadProc()
        {
            try
            {
                Log(LogEventTypeEnum.Info, "Engine thread running");
                _State = EngineStateEnum.Started;
                int num = 0;
                while (ExitEvent.WaitOne(500) == false)
                {
                    try
                    {
                        StartNetworking();
                        if (num > 10)
                        {
                            WriteDumps();
                            num = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(LogEventTypeEnum.Error, "Exception on ThreadProc: " + ex.Message + " @ " + ex.StackTrace);
                    }
                    num++;
                }
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on ThreadProc: " + ex.Message + " @ " + ex.StackTrace);
            }
            Log(LogEventTypeEnum.Info, "Engine thread stopping");
            Stop();
        }

        private void StartConnecting()
        {
            lock (socketLock)
            {
                if (remoteSocket != null)
                {
                    return;
                }
                connSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }
            try
            {
                StatsNetworkState = NetworkStateEnum.Connecting;
                Log(LogEventTypeEnum.Info, "Attempting to connect to " + cfg.Host + ":" + cfg.Port);
                connSocket.Connect(cfg.Host, cfg.Port);
                lock (socketLock)
                {
                    remoteSocket = connSocket;
                }
                Log(LogEventTypeEnum.Info, "Remote connection established");
                if (recvStream != "")
                {
                    Log(LogEventTypeEnum.Warning, "Discarding " + recvStream.Length + " byte(s) from stream buffer");
                    recvStream = "";
                }
                StatsNetworkState = NetworkStateEnum.Connected;
                StartReceiving();
                return;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on StartConnecting: " + ex.Message + " @ " + ex.StackTrace);
            }
            StatsNetworkState = NetworkStateEnum.Disconnected;
        }

        private void StartListening()
        {
            SocketAsyncEventArgs ex = new SocketAsyncEventArgs();
            ex.Completed += Accept_Completed;
            lock (socketLock)
            {
                if (listenSocket != null || remoteSocket != null)
                {
                    return;
                }
                Log(LogEventTypeEnum.Info, "Listening to connections on port " + cfg.Port);
                StatsNetworkState = NetworkStateEnum.Listening;
                listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, cfg.Port));
            listenSocket.Listen(0);
            listenSocket.AcceptAsync(ex);
        }

        private void StartReceiving()
        {
            try
            {
                SocketAsyncEventArgs ex = new SocketAsyncEventArgs();
                ex.SetBuffer(recvBuffer, 0, recvBuffer.Length);
                ex.Completed += new EventHandler<SocketAsyncEventArgs>(Receive_Completed);
                remoteSocket.ReceiveAsync(ex);
                StatsReceivesTriggered++;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on StartReceiving: " + ex.Message + " @ " + ex.StackTrace);
                StopNetworking();
            }
        }

        private void Receive_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    if (e.BytesTransferred == 0)
                    {
                        throw new SocketException((int)SocketError.ConnectionReset);
                    }
                    StatsBytesReceived += e.BytesTransferred;
                    recvStream += ASCIIEncoding.ASCII.GetString(recvBuffer, 0, e.BytesTransferred);
                    while (ProcessRecvStream() == true) ;
                    StartReceiving();
                }
                else
                {
                    throw new SocketException((int)e.SocketError);
                }
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on Receive_Completed: " + ex.Message + " @ " + ex.StackTrace);
                StopNetworking();
            }
        }

        private void Accept_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success)
                {
                    throw new SocketException((int)e.SocketError);
                }
                lock (socketLock)
                {
                    if (remoteSocket != null)
                    {
                        Log(LogEventTypeEnum.Warning, "Closing existing remote connection");
                        remoteSocket.Shutdown(SocketShutdown.Both);
                        remoteSocket.Close();
                        remoteSocket = null;
                    }
                    Log(LogEventTypeEnum.Info, "Remote connection established");
                    remoteSocket = e.AcceptSocket;
                }
                StatsNetworkState = NetworkStateEnum.Connected;
                if (recvStream != "")
                {
                    Log(LogEventTypeEnum.Warning, "Discarding " + recvStream.Length + " byte(s) from stream buffer");
                    recvStream = "";
                }
                StartReceiving();
                return;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on Accept_Completed: " + ex.Message + " @ " + ex.StackTrace);
            }
            StatsNetworkState = NetworkStateEnum.Disconnected;
        }

        private bool ProcessRecvStream()
        {
            Match m = rexCapture.Match(recvStream);
            if (m.Success == true)
            {
                if (m.Index > 0)
                {
                    Log(LogEventTypeEnum.Warning, recvStream.Length + " byte(s) garbage received");
                }
                string msg = m.Groups["MessageData"].Value;
                ProcessRecvMessage(msg);
                recvStream = recvStream.Substring(m.Index + m.Length);
                return true;
            }
            else if (recvStream.Length >= 1024 * 1024)
            {
                Log(LogEventTypeEnum.Warning, recvStream.Length + " byte(s) received without valid frame, flushing");
                recvStream = "";
            }
            return false;
        }

        private void ProcessRecvMessage(string msg)
        {
            msg = Base64Decode(msg);
            List<GameEvent> evs = new JavaScriptSerializer().Deserialize<List<GameEvent>>(msg);
            if (evs == null || evs.Count == 0)
            {
                Log(LogEventTypeEnum.Warning, "Received message without game events");
                return;
            }
            foreach (GameEvent ge in evs)
            {
                QueueGameEvent(ge);
            }
        }

        private void WriteDumps()
        {
            StringBuilder sb;
            if (cfg.DumpParsed == true)
            {
                sb = new StringBuilder();
                lock (recvParsedQueue)
                {
                    foreach (GameEvent ge in recvParsedQueue)
                    {
                        sb.AppendLine(ge.EventText);
                    }
                    recvParsedQueue.Clear();
                }
                string fn = "Parsed_Snoopy_" + dumpFileName + ".txt";
                fn = Path.Combine(cfg.DumpFolderParsed, fn);
                WriteDumpFile(fn, sb.ToString());
            }
            if (cfg.DumpNetwork == true)
            {
                sb = new StringBuilder();
                lock (recvNetworkQueue)
                {
                    foreach (GameEvent ge in recvNetworkQueue)
                    {
                        sb.AppendLine(ge.EventText);
                    }
                    recvNetworkQueue.Clear();
                }
                string fn = "Network_Snoopy_" + dumpFileName + ".log";
                fn = Path.Combine(cfg.DumpFolderNetwork, fn);
                WriteDumpFile(fn, sb.ToString());
            }
        }

        private void WriteDumpFile(string fn, string content)
        {
            using (StreamWriter sw = new StreamWriter(fn, true))
            {
                sw.Write(content);
                sw.Flush();
            }
            
        }

    }

}
