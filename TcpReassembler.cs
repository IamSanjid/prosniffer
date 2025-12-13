namespace PROSniffer
{
    public class TcpReassembler
    {
        private readonly TcpDirectionState _toServer = new();
        private readonly TcpDirectionState _toClient = new();

        private readonly int _remotePort;

        public TcpReassembler(int remotePort)
        {
            _remotePort = remotePort;
        }

        /// <summary>
        /// Call this from OnPacketArrival, passing the parsed TcpPacket.
        /// onToServer / onToClient are where you hook your RC4 logic.
        /// </summary>
        public void ProcessTcpPacket(PacketDotNet.TcpPacket tcp,
                                     PacketDotNet.IPPacket ip,
                                     Action<byte[]> onToServer,
                                     Action<byte[]> onToClient)
        {
            var payload = tcp.PayloadData;
            if (payload == null || payload.Length == 0)
                return; // pure ACK etc.

            // Decide direction using your RemotePort logic
            bool toServer = tcp.DestinationPort == _remotePort || tcp.SourcePort != _remotePort;
            var dirState = toServer ? _toServer : _toClient;
            var callback = toServer ? onToServer : onToClient;

            uint seq = (uint)tcp.SequenceNumber;
            int len = payload.Length;

            // First packet in this direction: initialize stream
            if (!dirState.NextSeq.HasValue)
            {
                dirState.NextSeq = seq + (uint)len;
                callback(payload);          // in-order by definition
                FlushBuffer(dirState, callback);
                return;
            }

            uint next = dirState.NextSeq.Value;

            if (seq == next)
            {
                // Exactly the next expected segment → in-order
                callback(payload);
                dirState.NextSeq = next + (uint)len;
                FlushBuffer(dirState, callback);
            }
            else if (seq > next)
            {
                // Future segment → out-of-order, buffer it if not already present
                if (!dirState.Buffer.ContainsKey(seq))
                {
                    // Store a copy to be safe
                    var copy = new byte[len];
                    Array.Copy(payload, copy, len);
                    dirState.Buffer[seq] = copy;
                }
            }
            else
            {
                // seq < next → old segment or retransmission; already processed
                // Just drop it so we don't advance RC4 twice on the same bytes
                return;
            }
        }

        private void FlushBuffer(TcpDirectionState state, Action<byte[]> callback)
        {
            // While we have a buffered segment whose start seq matches NextSeq, emit it
            while (state.NextSeq.HasValue &&
                   state.Buffer.TryGetValue(state.NextSeq.Value, out var payload))
            {
                state.Buffer.Remove(state.NextSeq.Value);
                callback(payload);
                state.NextSeq += (uint)payload.Length;
            }
        }
    }

}
