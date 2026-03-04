using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Model.MITSUBISHI
{
    public class DetailsData
    {
        private static readonly Lazy<DetailsData> lazy = new Lazy<DetailsData>(() => new DetailsData());
        public static DetailsData Instance => lazy.Value;
        public bool isMachineRunning { get; set; }
        public bool isMachineStop { get; set; }
    }
}
