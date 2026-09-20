

#ifndef SYSTEM_MONITOR_H_
#define SYSTEM_MONITOR_H_

#include "app_types.h"
//3.1
CommStatusUpdateResult_t UpdateCommunicationStatus(CommunicationStatus_t commstatus);

//3.2
FuelStatus_t CheckFuelStatus(float current_fuel);

//3.3
SystemStatus_t GetSystemStatus(void);
#endif
