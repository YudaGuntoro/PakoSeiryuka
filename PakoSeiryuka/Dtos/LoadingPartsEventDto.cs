using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Dtos
{
    public class LoadingPartsEventDto
    {
        public short LoadMachineNo { get; set; }
        public DateTime Time { get; set; }  // optional, tapi berguna untuk log
    }
}
