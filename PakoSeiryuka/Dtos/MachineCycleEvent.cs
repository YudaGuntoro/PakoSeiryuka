using PakoSeiryuka.Model.SIEMENS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class MachineCycleEvent
    {
        public int MachineNo { get; set; }
        public string TypeProduct { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public int Mold { get; set; }
        public int SideMold { get; set; }
        public bool MachineOn { get; set; }
        public string? CreationDateTime { get; set; } // ISO string
        public int[] TemperatureMold { get; set; } = new int[4];
        public int CounterProduct { get; set; }
        public int CycleTime { get; set; }
        public bool StartStopSignal { get; set; }
        public string Group { get; set; } = "";
        public float MetalWeight { get; set; }
        public CoolingBlock Cooling { get; set; } = new();
    }
}