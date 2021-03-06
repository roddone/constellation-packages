﻿using Constellation;
using Constellation.Package;
using static XiaomiSmartHome.Enums;

namespace XiaomiSmartHome.Equipement
{
    /// <summary>
    /// Smart Wireless Switch
    /// </summary>
    [StateObject]
    public class Switch : Equipment
    {
        /// <summary>
        /// Ctor
        /// </summary>
        public Switch()
        {
            base.Battery = BatteryType.CR1632;
        }

        /// <summary>
        /// Update equipment with last data
        /// </summary>
        public override void Update(object data)
        {
            Switch curData = data as Switch;
            if (curData.Voltage != default(int))
            {
                this.Voltage = curData.Voltage;
                this.BatteryLevel = base.ParseVoltage(curData.Voltage);
            }

            this.Status = curData.Status;
        }
    }
}