namespace PROSniffer
{
    public class TcpDirectionState
    {
        // Next sequence number we expect in-order for this direction
        public uint? NextSeq;

        // Out-of-order segments: key = seqStart, value = payload
        public SortedDictionary<uint, byte[]> Buffer = [];
    }
}
