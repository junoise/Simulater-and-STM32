#ifndef MISSION_MANAGER_H_
#define MISSION_MANAGER_H_

#include "app_types.h"

//3.1
MissionProcessResult_t ProcessMission(const MissionInput_t *mission_input,const SystemStatus_t *SystemStatus);

MissionState_t GetMissionState(void);

bool GetTargetCommand(TargetCommand_t *target_command);
#endif
