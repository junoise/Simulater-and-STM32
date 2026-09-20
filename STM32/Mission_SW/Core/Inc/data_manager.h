

#ifndef DATA_MANAGER_H_
#define DATA_MANAGER_H_

#include "app_types.h"

AircraftValidationResult_t ValidateAircraftState(const AircraftState_t *aircraftstate);
//상세설계 3.1

DestinationValidationResult_t ValidateDestination(const Destination_t *destination);
//상세설계 3.2

MissionCommandValidationResult_t ValidateMissionCommand(uint8_t mission_start_command);
//상세설꼐 3.3

DataUpdateResult_t UpdateData(const RxMessage_t *rx_message);
//상세설계 3.4

MissionInputResult_t CreateMissionInput(MissionInput_t *missioninput);
//상세설계 3.5

DataStatus_t GetDataStatus(void);
//상세설계 3.6
bool GetCurrentFuel(float *fuel);
//상세설계 3.7
#endif
