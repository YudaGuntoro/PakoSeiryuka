using System;

namespace PakoSeiryuka.Model.SIEMENS
{
    public class DetailsData
    {
        // ================= Machine basic =================
        public int MachineNo { get; set; }
        public string TypeProduct { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public int Mold { get; set; }
        public int SideMold { get; set; }
        public bool MachineOn { get; set; }

        // ================= Creation date (DTL) =================
        public ushort CreationYear { get; set; }
        public byte CreationMonth { get; set; }
        public byte CreationDay { get; set; }
        public byte CreationWeekday { get; set; }
        public byte CreationHour { get; set; }
        public byte CreationMinute { get; set; }
        public byte CreationSecond { get; set; }
        public uint CreationNanosecond { get; set; }

        public DateTime? CreationDateTime
        {
            get
            {
                try
                {
                    if (CreationYear == 0 || CreationMonth == 0 || CreationDay == 0) return null;
                    return new DateTime(CreationYear, CreationMonth, CreationDay, CreationHour, CreationMinute, CreationSecond);
                }
                catch
                {
                    return null;
                }
            }
        }

        // ================= Temperature Mold =================
        // PER MACHINE (isi 4 value). Mapping kamu:
        // Mesin1 -> C1, Mesin2 -> C2, Mesin3 -> C3, Mesin4 -> C4, Mesin5 -> C5
        public int[] TemperatureMold { get; } = new int[4];

        // ================= Temperature metal =================
        // GLOBAL (di snapshot cukup 1 data). Kalau mau tetap ditempel ke tiap machine boleh, tapi bukan wajib.
        // ================= Counter / Cycle / StartStop / Group =================
        public int CounterProduct { get; set; }
        public int CycleTime { get; set; }
        public bool StartStopSignal { get; set; }
        public string Group { get; set; } = "";
        public float MetalWeight { get; set; }
        // ================= Abnormality =================
        // GLOBAL 10 item (biasanya sama untuk semua machine). Boleh tetap ditempel di tiap machine.
        public bool[] MachineAlarms { get; } = new bool[48]; // ID 1..48


        // ================= Cooling =================
        // PER MACHINE: 1 mesin = 1 cooling block
        public CoolingBlock Cooling { get; set; } = new CoolingBlock();

    }

    public class CoolingBlock
    {
        public int WaitingAir1 { get; set; }
        public int WaitingAir2 { get; set; }
        public int WaitingAir3 { get; set; }
        public int WaitingAir4 { get; set; }
        public int WaitingWater1 { get; set; }
        public int WaitingWater2 { get; set; }

        public int CoolingAir1 { get; set; }
        public int CoolingAir2 { get; set; }
        public int CoolingAir3 { get; set; }
        public int CoolingAir4 { get; set; }
        public int CoolingWater1 { get; set; }
        public int CoolingWater2 { get; set; }

        public int AirPressure1 { get; set; }
        public int AirPressure2 { get; set; }
        public int AirPressure3 { get; set; }
        public int AirPressure4 { get; set; }

        public int FlowRate1 { get; set; }
        public int FlowRate2 { get; set; }
        public int FlowRate3 { get; set; }
        public int FlowRate4 { get; set; }
        public int FlowRate5 { get; set; }
        public int FlowRate6 { get; set; }
    }
}
