using SharpPcap;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PROSniffer
{
    public class PortSniffer
    {
        public static IEnumerable<string> GetInterfaces()
        {
            return CaptureDeviceList.Instance.Select(x => x.Description);
        }

        private readonly ILiveDevice _selectedDevice;

        private string _receiveBuffer = string.Empty;
        private readonly Queue<string> _pendingRecvPackets = new();

        private string _sentBuffer = string.Empty;
        private readonly Queue<string> _pendingSentPackets = new();

        private readonly ConcurrentQueue<byte[]> _recvQueuePackets = new();
        private readonly ConcurrentQueue<byte[]> _sendQueuePackets = new();

        public ushort RemotePort { get; }
        public int DeviceIndex { get; }
        public string? CustomFilter { get; }
        public bool HasStarted => _selectedDevice != null && _selectedDevice.Started;

        protected string PacketDelimiter = "\n";
        protected Encoding TextEncoding = Encoding.UTF8;

        public event Action<string>? PacketReceived;
        public event Action<string>? SentPacket;

        public PortSniffer(int deviceIndex, ushort remotePort = 0, string? customFilter = null)
        {
            _selectedDevice = CaptureDeviceList.Instance[deviceIndex];
            DeviceIndex = deviceIndex;

            RemotePort = remotePort;
            CustomFilter = customFilter;
            _selectedDevice.OnPacketArrival += OnPacketArrival;
        }

        private void OnPacketArrival(object sender, PacketCapture e)
        {
            //var time = e.Header.Timeval.Date;
            //var len = e.Data.Length;
            var rawPacket = e.GetPacket();

            var packet = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

            var tcpPacket = packet.Extract<PacketDotNet.TcpPacket>();
            if (tcpPacket != null && tcpPacket.PayloadData != null && tcpPacket.PayloadData.Length > 0)
            {
                //var ipPacket = (PacketDotNet.IPPacket)tcpPacket.ParentPacket;
                //System.Net.IPAddress srcIp = ipPacket.SourceAddress;
                //System.Net.IPAddress dstIp = ipPacket.DestinationAddress;
                int srcPort = tcpPacket.SourcePort;
                //int dstPort = tcpPacket.DestinationPort;

                byte[] packetsData = new byte[tcpPacket.PayloadData.Length];
                Array.Copy(tcpPacket.PayloadData, packetsData, tcpPacket.PayloadData.Length);
                if (srcPort == RemotePort)
                {
                    // Receiving from server...
                    _recvQueuePackets.Enqueue(packetsData);
                }
                else
                {
                    // Sending to server...
                    _sendQueuePackets.Enqueue(packetsData);
                }
            }
        }

        public void StartSniffing(int readTimeout = 1000)
        {
            if (HasStarted)
            {
                return;
            }
            _selectedDevice.Open(DeviceModes.Promiscuous, readTimeout);
            if (CustomFilter != null)
            {
                _selectedDevice.Filter = CustomFilter;
            }
            else
            {
                _selectedDevice.Filter = $"port {RemotePort}";
            }
            _selectedDevice.StartCapture();
        }

        public void StopSniffing()
        {
            _selectedDevice.StopCapture();
            _selectedDevice.Close();
        }

        public void Update()
        {
            if (!HasStarted)
            {
                return;
            }

            while (_recvQueuePackets.TryDequeue(out var bytes))
            {
                OnPacketReceived(bytes);
            }

            while (_sendQueuePackets.TryDequeue(out var bytes))
            {
                OnPacketSent(bytes);
            }

            ReceivePendingPackets();
        }

        protected virtual byte[] ProcessDataBeforeSending(byte[] data)
        {
            return data;
        }

        protected virtual byte[] ProcessDataBeforeReceiving(byte[] data)
        {
            return data;
        }

        protected virtual byte[] ProcessSentDataBeforeReceiving(byte[] data)
        {
            return data;
        }

        protected virtual string ProcessPacketBeforeSending(string packet)
        {
            return packet;
        }

        protected virtual string ProcessPacketBeforeReceiving(string packet)
        {
            return packet;
        }

        protected virtual string ProcessSentPacketBeforeReceiving(string packet)
        {
            return packet;
        }

        private void ReceivePendingPackets()
        {
            bool hasSent;
            do
            {
                string? packet = null;
                lock (_pendingSentPackets)
                {
                    if (_pendingSentPackets.Count > 0)
                    {
                        packet = _pendingSentPackets.Dequeue();
                    }
                }
                hasSent = false;
                if (packet != null)
                {
                    hasSent = true;
                    SentPacket?.Invoke(packet);
                }
            }
            while (hasSent);

            bool hasReceived;
            do
            {
                string? packet = null;
                lock (_pendingRecvPackets)
                {
                    if (_pendingRecvPackets.Count > 0)
                    {
                        packet = _pendingRecvPackets.Dequeue();
                    }
                }
                hasReceived = false;
                if (packet != null)
                {
                    hasReceived = true;
                    PacketReceived?.Invoke(packet);
                }
            }
            while (hasReceived);
        }

        private void ExtractRecvPackets()
        {
            bool hasExtracted;
            do
            {
                hasExtracted = ExtractPendingRecvPacket();
            }
            while (hasExtracted);
        }

        private bool ExtractPendingRecvPacket()
        {
            int pos = _receiveBuffer.IndexOf(PacketDelimiter);
            if (pos >= 0)
            {
                string packet = _receiveBuffer[..pos];
                _receiveBuffer = _receiveBuffer[(pos + PacketDelimiter.Length)..];
                lock (_pendingRecvPackets)
                {
                    _pendingRecvPackets.Enqueue(packet);
                }
                return true;
            }
            return false;
        }

        private void ExtractSentPackets()
        {
            bool hasExtracted;
            do
            {
                hasExtracted = ExtractPendingSentPacket();
            }
            while (hasExtracted);
        }

        private bool ExtractPendingSentPacket()
        {
            int pos = _sentBuffer.IndexOf(PacketDelimiter);
            if (pos >= 0)
            {
                string packet = _sentBuffer[..pos];
                _sentBuffer = _sentBuffer[(pos + PacketDelimiter.Length)..];
                lock (_pendingSentPackets)
                {
                    _pendingSentPackets.Enqueue(packet);
                }
                return true;
            }
            return false;
        }

        private void OnPacketReceived(byte[] data)
        {
            string text = TextEncoding.GetString(ProcessDataBeforeReceiving(data));
            _receiveBuffer += text;
            ExtractRecvPackets();
        }

        private void OnPacketSent(byte[] data)
        {
            string text = TextEncoding.GetString(ProcessSentDataBeforeReceiving(data));
            _sentBuffer += text;
            ExtractSentPackets();
        }
    }
}
