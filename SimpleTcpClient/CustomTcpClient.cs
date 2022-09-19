using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

//ORIGIN:
//https://github.com/BrandonPotter/SimpleTCP

namespace SimpleTcpClient
{

    public class CustomTcpClient : IDisposable
    {

        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public TcpClient TcpClient { get { return _client; } }

        protected byte[] Delimiter { get; set; } = new byte[] { 0x13 };
        protected Encoding StringEncoder { get; set; } = Encoding.ASCII;
        protected int ReadLoopIntervalMs { get; set; } = 10;
        protected bool AutoTrimStrings { get; set; }

        protected bool IsCanSkipFirstBurst { get; set; } = false;
        

        private Thread _rxThread = null;
        private List<byte> _queuedMsg = new List<byte>();
        private TcpClient _client = null;

        public event EventHandler<string> OnDelimiterDataReceived;
        public event EventHandler<string> OnDataReceived;
        public event EventHandler OnConnected;
        public event EventHandler OnDisconnected;

        internal bool QueueStop { get; set; }
        
        private string DestIp { get; set; }
        private int DestPort { get; set; }

        private static object _locker = new object();
        private System.Timers.Timer CheckConnT = new System.Timers.Timer();
        private bool IsConnected = false;

        public CustomTcpClient()
        {
            CheckConnT.Elapsed += new ElapsedEventHandler(OnCheckConnT);
            CheckConnT.Interval = 5000;
            CheckConnT.Enabled = false;
            CheckConnT.AutoReset = true;
        }

        #region FluentApi

        public CustomTcpClient SetIp(string Ip)
        {
            if (string.IsNullOrEmpty(Ip))
            {
                throw new ArgumentNullException("Ip cannot be NULL or Empty!");
            }

            DestIp = Ip;

            return this;
        }

        public CustomTcpClient SetPort(int Port)
        {
            if (Port <= 0)
            {
                throw new ArgumentNullException("Port cannot be <= 0!");
            }

            DestPort = Port;

            return this;
        }

        public CustomTcpClient SetConnData(string Ip, int Port)
        {
            SetIp(Ip);
            SetPort(Port);
            return this;
        }

        /// <summary>
        /// How Fast we will check for new Data arrived
        /// </summary>
        /// <param name="Freq"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CustomTcpClient SetMsgPollFreq(int Freq)
        {
            if (Freq <= 0)
            {
                throw new ArgumentNullException("Polling Frequency of new Messages cannot be <= 0!");
            }

            ReadLoopIntervalMs = Freq;

            return this;
        }

        /// <summary>
        /// Delimiter to capture End of a Message
        /// </summary>
        /// <param name="Delimiter"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CustomTcpClient SetDelimiter(byte[] Delimiter)
        {
            if (Delimiter == null || Delimiter.Length == 0)
            {
                throw new ArgumentNullException("Delimiter cannot be NULL or Empty!");
            }

            this.Delimiter = Delimiter;

            return this;
        }

        /// <summary>
        /// Delimiter to capture End of a Message (will transform to byte[] through supplied Encoder)
        /// </summary>
        /// <param name="Delimiter"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CustomTcpClient SetDelimiter(string Delimiter)
        {
            if (string.IsNullOrEmpty(Delimiter) == true)
            {
                throw new ArgumentNullException("Delimiter cannot be NULL or Empty!");
            }

            this.Delimiter = StringEncoder.GetBytes(Delimiter);

            return this;
        }

        /// <summary>
        /// Encoder to be used to generate String
        /// </summary>
        /// <param name="Encoder"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CustomTcpClient SetEncoder(Encoding Encoder)
        {
            if (Encoder == null)
            {
                throw new ArgumentNullException("Encoder cannot be NULL!");
            }

            //in case Encoder is setted after Delimiter property in FluentApi
            var oldDelimiter = StringEncoder.GetString(this.Delimiter);

            this.StringEncoder = Encoder;

            this.Delimiter = Encoder.GetBytes(oldDelimiter);

            return this;
        }

        /// <summary>
        /// How often we will check if Device is still alive
        /// </summary>
        /// <param name="Milliseconds"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CustomTcpClient SetConnCheckInterval(int Milliseconds)
        {
            if (Milliseconds < 1000)
            {
                throw new ArgumentNullException("Milliseconds cannot be <= 1000!");
            }

            CheckConnT.Interval = Milliseconds;

            return this;
        }

        /// <summary>
        /// Set to True if Device will attempt to push on you Data previously readed while you were offline and you dont want it
        /// </summary>
        /// <param name="IsCanSkip"></param>
        /// <returns></returns>
        public CustomTcpClient SetSkipFirstBurst(bool IsCanSkip)
        {
            this.IsCanSkipFirstBurst = IsCanSkip;
            return this;
        }

        public CustomTcpClient SetAutoTrim(bool IsCanTrim)
        {
            this.AutoTrimStrings = IsCanTrim;
            return this;
        }

        #endregion

        public bool Connect()
        {
            try
            {

                log.Debug($"Connect Invoked! {DestIp}:{DestPort}");

                _client = new TcpClient();
                _client.Connect(DestIp, DestPort);

                StartRxThread();

                CheckConnT?.Stop();
                CheckConnT?.Start();

                IsConnected = true;
                OnConnected?.Invoke(this, null);

                return true;

            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }
        }

        public bool Disconnect()
        {
            try
            {

                log.Debug("Disconnect Invoked!");

                _client?.Close();
                _client = null;

                OnDisconnected?.Invoke(this, null);
                IsConnected = false;

                return true;

            }
            catch(Exception ex)
            {
                log.Error(ex);
                return false;
            }
        }

        private void StartRxThread()
        {
            if (_rxThread != null) { return; }

            _rxThread = new Thread(ListenerLoop);
            _rxThread.IsBackground = true;
            _rxThread.Start();
        }

        private void ListenerLoop(object state)
        {
            while (!QueueStop)
            {
                try
                {
                    RunLoopStep();
                }
                catch(Exception ex)
                {
                    log.Error(ex);
                }

                Thread.Sleep(ReadLoopIntervalMs);
            }

            _rxThread = null;
        }

        private void RunLoopStep()
        {
            if (_client == null)
            {
                return;
            }
            else if (_client.Connected == false)
            {
                return;
            }

            //Wait until new Data is available
            if (_client.Available == 0)
            {
                Thread.Sleep(25);
                //can be disposed meanwhile
                if (_client?.Available == 0)
                {
                    //if still nothing, probably nothing will arrive
                    DenySkippingMessages();
                }
                return;
            }

            List<byte> bytesReceived = new List<byte>();

            //this vars are needed to detect delimiter
            int delimCnt = 0;
            //will become true if whole delimiter sequence matched
            bool delimMatch = false;

            while (_client.Available > 0 && _client.Connected)
            {
                byte[] nextByte = new byte[1];
                _client.Client.Receive(nextByte, 0, 1, SocketFlags.None);
                bytesReceived.AddRange(nextByte);

                //DelimiterMatcher
                if (nextByte[0] == Delimiter[delimCnt])
                {
                    delimCnt++;
                    if (delimCnt == Delimiter.Length)
                    {
                        delimMatch = true;
                    }
                }

                if (delimMatch == true)
                {
                    byte[] msg = _queuedMsg.ToArray();
                    _queuedMsg.Clear();
                    NotifyDelimiterMessageRx(_client, msg);
                    delimMatch = false;
                    delimCnt = 0;
                }
                else
                {
                    _queuedMsg.AddRange(nextByte);
                }
            }

            DenySkippingMessages();

            if (bytesReceived.Count > 0)
            {
                NotifyEndTransmissionRx(_client, bytesReceived.ToArray());
            }
        }

        private void DenySkippingMessages()
        {
            if (IsCanSkipFirstBurst == true)
            {
                IsCanSkipFirstBurst = false;
            }
        }

        private void NotifyDelimiterMessageRx(TcpClient client, byte[] msg)
        {
            if (OnDelimiterDataReceived != null)
            {

                if (IsCanSkipFirstBurst == true)
                {
                    //will set to false later
                    return;
                }

                var stringMsg = StringEncoder.GetString(msg);
                if(AutoTrimStrings == true)
                {
                    stringMsg = stringMsg.Trim();
                }

                //Message m = new Message(msg, client, StringEncoder, Delimiter, AutoTrimStrings);
                OnDelimiterDataReceived(this, stringMsg);

            }
        }

        private void NotifyEndTransmissionRx(TcpClient client, byte[] msg)
        {
            if (OnDataReceived != null)
            {

                //Message m = new Message(msg, client, StringEncoder, Delimiter, AutoTrimStrings);
                
                var stringMsg = StringEncoder.GetString(msg);
                if (AutoTrimStrings == true)
                {
                    stringMsg = stringMsg.Trim();
                }

                OnDataReceived(this, stringMsg);

            }
        }

        public void Write(byte[] data)
        {
            if (_client == null) { throw new Exception("Cannot send data to a null TcpClient (check to see if Connect was called)"); }
            _client.GetStream().Write(data, 0, data.Length);
        }

        public void Write(string data)
        {
            if (data == null) { return; }
            Write(StringEncoder.GetBytes(data));
        }

        public void WriteLine(string data)
        {
            if (string.IsNullOrEmpty(data)) { return; }

            var lastChars = data.TakeLast(Delimiter.Length).ToList();

            bool matchingDelimiter = true;
            for (int i = 0; i < lastChars.Count; i++)
            {
                if (lastChars[i] != Delimiter[i])
                {
                    matchingDelimiter = false;
                    break;
                }
            }

            if (matchingDelimiter == false)
            {
                Write(data + StringEncoder.GetString(Delimiter));
            }
            else
            {
                Write(data);
            }
        }

        //public Message WriteLineAndGetReply(string data, TimeSpan timeout)
        //{
        //    Message mReply = null;
        //    this.DataReceived += (s, e) => { mReply = e; };
        //    WriteLine(data);

        //    Stopwatch sw = new Stopwatch();
        //    sw.Start();

        //    while (mReply == null && sw.Elapsed < timeout)
        //    {
        //        Thread.Sleep(10);
        //    }

        //    return mReply;
        //}

        public string WriteLineAndGetReply(string data, TimeSpan timeout)
        {
            string mReply = null;
            this.OnDataReceived += (s, e) => { mReply = e; };
            WriteLine(data);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (mReply == null && sw.Elapsed < timeout)
            {
                Thread.Sleep(10);
            }

            return mReply;
        }

        #region Connection / Disconnection

        private bool PingHost()
        {
            bool pingable = false;
            Ping pinger = null;

            try
            {
                pinger = new Ping();
                PingReply reply = pinger.Send(DestIp);
                pingable = reply.Status == IPStatus.Success;
            }
            catch (PingException pe)
            {
                // Discard PingExceptions and return false;
                log.Trace(pe);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                if (pinger != null)
                {
                    pinger.Dispose();
                }
            }

            return pingable;
        }

        private void OnCheckConnT(object source, ElapsedEventArgs e)
        {
            //prevent multiple enterings
            var hasLock = false;

            try
            {
                Monitor.TryEnter(_locker, ref hasLock);
                if (!hasLock)
                {
                    return;
                }

                log.Trace($"Pinging Device: {DestIp}");

                // Do something
                var isReachable = PingHost();

                log.Trace($"Ping result: isReachable: {isReachable}, IsConnected: {IsConnected}");

                if (isReachable == false && IsConnected == true)
                {
                    log.Trace("Disconnection Required!");
                    Disconnect();
                }
                else
                {
                    if (IsConnected == false)
                    {
                        log.Trace("ReConnection Required!");
                        Connect();
                    }
                }

            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                if (hasLock)
                {
                    Monitor.Exit(_locker);
                }
            }
        }

        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).

                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                QueueStop = true;
                if (_client != null)
                {
                    try
                    {
                        _client.Close();
                    }
                    catch { }
                    _client = null;
                }

                CheckConnT?.Stop();

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SimpleTcpClient() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }

}
