using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SnoopyPlugin
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
        public int StatsSendsTriggered { get; set; } = 0;
        public int StatsBytesSent { get; set; } = 0;
        public DateTime StatsLatestLogEvent { get; set; } = DateTime.MinValue;
        public NetworkStateEnum StatsNetworkState { get; set; } = NetworkStateEnum.Disconnected;

        internal Configuration cfg { get; set; }

        private Socket listenSocket = null;
        private Socket connSocket = null;
        private Socket remoteSocket = null;
        private object socketLock = new object();
        internal List<LogEvent> log = new List<LogEvent>();
        private List<GameEvent> sendBuffer = new List<GameEvent>();
        private Thread EngineThread = null;
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
            StatsSendsTriggered = 0;
            StatsBytesSent = 0;
            StatsNetworkState = NetworkStateEnum.Disconnected;
        }

        public void QueueGameEvent(GameEvent ge)
        {
            lock (sendBuffer)
            {
                if (IsConnected() == true || cfg.QueueDisconnected == true)
                {
                    sendBuffer.Add(ge);
                    StatsEventsInQueue++;
                }
            }
            if (ge.IsNetwork == true)
            {
                StatsEventsNetworkSeen++;
            }
            else
            {
                StatsEventsParsedSeen++;
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
                lock (sendBuffer)
                {
                    sendBuffer.Clear();
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
            catch (Exception)
            {
            }
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
                while (ExitEvent.WaitOne(500) == false)
                {
                    try
                    {
                        StartNetworking();
                        SendData();
                    }
                    catch (Exception ex)
                    {
                        Log(LogEventTypeEnum.Error, "Exception on ThreadProc: " + ex.Message + " @ " + ex.StackTrace);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on ThreadProc: " + ex.Message + " @ " + ex.StackTrace);
            }
            Log(LogEventTypeEnum.Info, "Engine thread stopping");
            Stop();
        }

        public static string Base64Encode(string str)
        {
            byte[] bs = System.Text.Encoding.UTF8.GetBytes(str);
            return System.Convert.ToBase64String(bs);
        }

        private void SendData()
        {
            List<GameEvent> stuff = new List<GameEvent>();
            if (cfg.QueueDisconnected == true && IsConnected() == false)
            {
                return;
            }
            lock (sendBuffer)
            {
                int num = sendBuffer.Count > 1000 ? 1000 : sendBuffer.Count;
                if (num > 0)
                {
                    stuff.AddRange(sendBuffer.Take(num));
                    sendBuffer.RemoveRange(0, num);
                }
                StatsEventsInQueue = 0;
            }
            if (stuff.Count == 0)
            {
                return;
            }
            var wh = new JavaScriptSerializer().Serialize(stuff);
            try
            {
                lock (socketLock)
                {
                    if (remoteSocket != null)
                    {
                        byte[] buf = System.Text.Encoding.ASCII.GetBytes("\x02" + Base64Encode(wh) + "\x03");
                        remoteSocket.Send(buf);
                        StatsSendsTriggered++;
                        StatsBytesSent += buf.Length;
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on SendData: " + ex.Message + " @ " + ex.StackTrace);
            }
            StopNetworking();
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
                StatsNetworkState = NetworkStateEnum.Connected;
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
                return;
            }
            catch (Exception ex)
            {
                Log(LogEventTypeEnum.Error, "Exception on Accept_Completed: " + ex.Message + " @ " + ex.StackTrace);
            }
            StatsNetworkState = NetworkStateEnum.Disconnected;
        }

    }

}
