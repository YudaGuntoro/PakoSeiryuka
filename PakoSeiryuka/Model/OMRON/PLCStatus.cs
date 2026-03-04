using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Model.OMRON
{
    public class PLCStatus
    {
        public string PLC_Ip { get; set; }
        public int PLC_Port { get; set; }
        public bool IsConnected { get; set; }
        public string MessageStatus { get; set; }
        public List<DetailsData> Data { get; set; } = new List<DetailsData>();
    }
}
