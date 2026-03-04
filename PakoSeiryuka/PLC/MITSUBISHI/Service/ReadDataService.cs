using HslCommunication.Profinet.Melsec;
using Microsoft.Extensions.Logging;
using PakoSeiryuka.Model.MITSUBISHI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace PakoSeiryuka.PLC.MITSUBISHI.Service
{
    public class ReadDataService
    {
        private MelsecMcNet plc;
        private System.Timers.Timer _timerLoop;  
        DetailsData data = new DetailsData();   
        public ReadDataService(MelsecMcNet plc)
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
                var isMachineRunning = plc.ReadBool("X1");
                if (isMachineRunning.IsSuccess)
                {
                    var Data = isMachineRunning.Content;
                    //data.isMachineRunning = Data;
                    DetailsData.Instance.isMachineRunning = Data;
                }

                var isMachineStop = plc.ReadBool("X2");
                if (isMachineStop.IsSuccess)
                {
                    var Data = isMachineStop.Content;
                    //data.isMachineStop = Data;
                    DetailsData.Instance.isMachineStop = Data;
                }
            }
            catch (Exception ex)
            {
               
            }
        }

        private int ReadInt16(MelsecMcNet plc,string Address)
        {
            return plc.ReadInt16(Address).Content;
        }
    }
}
