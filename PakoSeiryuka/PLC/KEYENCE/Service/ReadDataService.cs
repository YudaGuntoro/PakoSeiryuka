using HslCommunication.Core;
using HslCommunication.Profinet.Keyence;
using HslCommunication.Profinet.Melsec;
using PakoSeiryuka.Model.KEYENCE;
using PakoSeiryuka.Singletone;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace PakoSeiryuka.PLC.KEYENCE.Service
{
    public class ReadDataService
    {
        private KeyenceNanoSerialOverTcp plc;
        private System.Timers.Timer _timerLoop;
        DetailsData data = new DetailsData();
        public ReadDataService(KeyenceNanoSerialOverTcp plc)
        {
            this.plc = plc;

            // timer
            _timerLoop = new System.Timers.Timer();
            _timerLoop.Interval = 1 * 1;
            _timerLoop.Elapsed += _timerLoop_Elapsed;
            _timerLoop.Enabled = true;
        }
        private void _timerLoop_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var firstScanner = plc.ReadString("DM2200",10);
                if (firstScanner.IsSuccess)
                {
                    var Data = firstScanner.Content;
                    DetailsData.Instance.firstScanner = Data;
                 
                }
                var secondScanner = plc.ReadString("W05A", 6);
                if (secondScanner.IsSuccess)
                {
                    var Data = secondScanner.Content;
                    DetailsData.Instance.secondScanner = LittleEndianToBigEndian(Data);
                }
            }
            catch (Exception ex)
            {

            }
        }
        // Function to convert little-endian hex string to big-endian
        // Function to convert little-endian hex string to big-endian
        public static string LittleEndianToBigEndian(string littleEndianHex)
        {
            // Split the input hex string into byte pairs
            int numBytes = littleEndianHex.Length / 2;
            string[] bytes = new string[numBytes];

            for (int i = 0; i < numBytes; i++)
            {
                bytes[i] = littleEndianHex.Substring(i * 2, 2);
            }

            // Reverse the byte array
            Array.Reverse(bytes);

            // Reassemble the reversed bytes into a single string
            string bigEndianHex = string.Join("", bytes);

            // Reverse the entire string (if needed)
            char[] reversedCharArray = bigEndianHex.ToCharArray();
            Array.Reverse(reversedCharArray);

            return new string(reversedCharArray);
        }

        private int ReadInt16(MelsecMcNet plc, string Address)
        {
            return plc.ReadInt16(Address).Content;
        }
    }
}
