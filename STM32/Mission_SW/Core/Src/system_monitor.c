#include "system_monitor.h"

#define FUEL_LOW_THRESHOLD (20.0f) /* 임시 기준값 */

static SystemStatus_t latest_status = {
    .communication_status = COMM_ERROR,
    .fuel_status = FUEL_NORMAL
};

CommStatusUpdateResult_t UpdateCommunicationStatus(
        CommunicationStatus_t commstatus)
{
    if (commstatus != COMM_OK && commstatus != COMM_ERROR) {
        return COMM_STATUS_UPDATE_FAIL;
    }

    latest_status.communication_status = commstatus;

    return COMM_STATUS_UPDATE_SUCCESS;
}

//3.2
FuelStatus_t CheckFuelStatus(float current_fuel)
{
    if (current_fuel <= FUEL_LOW_THRESHOLD) {
        latest_status.fuel_status = FUEL_LOW;
    } else {
        latest_status.fuel_status = FUEL_NORMAL;
    }

    return latest_status.fuel_status;
}

//3.3
SystemStatus_t GetSystemStatus(void)
{
    return latest_status;
}
