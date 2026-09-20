

#ifndef OUTPUT_DATA_MANAGER_H_
#define OUTPUT_DATA_MANAGER_H_

#include "app_types.h"
//3.1
OutputUpdateResult_t UpdateOutputData(MissionState_t mission,const TargetCommand_t *target,uint8_t current_waypoint_index);

//3.2
WaypointOutputUpdateResult_t UpdateWaypointData(const WaypointList_t *waypointlist);

//3.3
TxMessageResult_t CreateTxMessage(TxMessage_t *txmessage);
#endif
