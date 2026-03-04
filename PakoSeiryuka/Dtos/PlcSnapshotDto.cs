using PakoSeiryuka.Model.SIEMENS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class PlcSnapshotDto
    {
        public bool LoadTrigger { get; set; }
        public bool UnloadTrigger { get; set; }
        public short LoadMachineNo { get; set; }
        public short UnloadMachineNo { get; set; }
        public int TemperatureMetal { get; set; }
        public bool[] GlobalAlarms { get; set; } = new bool[48]; // ID 500..547
        public string[] MaterialQueue { get; set; } = Array.Empty<string>();
        public List<DetailsData> Machines { get; set; } = new();
    }
}
