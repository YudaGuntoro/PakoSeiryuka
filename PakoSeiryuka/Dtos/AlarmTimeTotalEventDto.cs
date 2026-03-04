using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class AlarmTimeTotalEventDto
    {
        public int PLC { get; set; }          // ✅ NEW
        public int MachineNo { get; set; }
        public int AlarmIndex { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int TotalDuration { get; set; }

        // transparansi data
        public bool HasDisconnectGap { get; set; }
        public int DisconnectGapSeconds { get; set; }
    }
}
