using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PROSniffer
{
    public class RC4Sniffer : PortSniffer
    {
        private class State
        {
            public State(byte[] box)
            {
                _box = new byte[box.Length];
                Array.Copy(box, _box, box.Length);

                _i = 0;
                _j = 0;
            }

            public byte[] Crypt(IReadOnlyList<byte> bytes, int len)
            {
                byte[] new_bytes = new byte[len];
                var data = bytes.Take(len).ToArray();

                for (int i = 0; i < len; i++)
                {
                    _i = (_i + 1) % 256;
                    _j = (_j + _box[_i]) % 256;

                    byte temp_i = _box[_i];

                    _box[_i] = _box[_j];
                    _box[_j] = temp_i;

                    new_bytes[i] = (byte)(data[i] ^ _box[(_box[_i] + _box[_j]) % 256]);
                }

                return new_bytes;
            }

            private readonly byte[] _box;

            private int _i;
            private int _j;
        }

        private State? recvState;
        private State? sendState;

        public bool StateReady { get; private set; }

        public byte[] Encrypt(byte[] input, int len = -1)
        {
            if (sendState is null)
            {
                throw new NullReferenceException("Send RC4 Box was not initialized.");
            }
            return sendState.Crypt(input, len > 0 ? len : input.Length);
        }

        public byte[] Decrypt(byte[] input, int len = -1)
        {
            if (recvState is null)
            {
                throw new NullReferenceException("Receive RC4 Box was not initialized.");
            }
            return recvState.Crypt(input, len > 0 ? len : input.Length);
        }

        public RC4Sniffer(int deviceIdx, ushort port, string? customFilter = null) : base(deviceIdx, port, customFilter)
        {
            PacketDelimiter = "|.\\\r\n";
            TextEncoding = Encoding.GetEncoding("ISO-8859-1");
            Reset();
        }

        public void Reset()
        {
            sendState = new State(Default.SEND_KEY.Not());
            recvState = new State(Default.RECV_KEY);
            StateReady = false;
        }

        public void Initialize(byte[] initBytes)
        {
            if (initBytes.Length != 32)
            {
                throw new ArgumentException("The provided byte array must be 32 in length.");
            }
            Decrypt(initBytes, 16);
            Encrypt(initBytes.Skip(16).ToArray(), 16);
            StateReady = true;
        }

        public Encoding GetTextEncoding() => TextEncoding;

        protected override byte[] ProcessDataBeforeReceiving(byte[] data)
        {
            if (!StateReady)
            {
                Initialize(data);
                return Array.Empty<byte>();
            }
            return Decrypt(data);
        }

        protected override byte[] ProcessSentDataBeforeReceiving(byte[] data)
        {
            if (!StateReady)
            {
                throw new ArgumentException("RC4 State is not ready but yet trying to process sent data.");
            }
            return Encrypt(data);
        }
    }
}
