using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class UnloadingPartsEventDto
    {
        public short UnloadMachineNo { get; set; }
        public DateTime Time { get; set; }
    }
}
