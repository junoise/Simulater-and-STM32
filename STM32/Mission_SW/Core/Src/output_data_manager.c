#include "output_data_manager.h"
#include "data_manager.h"
#include <stddef.h>
static TxMessage_t latestmessage ={0};
static bool output_data_ready = false;

OutputUpdateResult_t UpdateOutputData(MissionState_t mission,const TargetCommand_t *target,uint8_t current_waypoint_index){

	if(target ==NULL){
		return OUTPUT_UPDATE_FAIL;
	}
	else{
		latestmessage.output_data.mission_state = mission;//1

			latestmessage.output_data.target_altitude = target->target_altitude;//2
			latestmessage.output_data.target_heading = target->target_heading;
			latestmessage.output_data.target_speed= target->target_speed;

			latestmessage.output_data.current_waypoint_index =current_waypoint_index;//3

			latestmessage.output_data.data_status = GetDataStatus();//4
			output_data_ready = true;
			return OUTPUT_UPDATE_SUCCESS;
	}

}

//3.2
WaypointOutputUpdateResult_t UpdateWaypointData(const WaypointList_t *waypointlist){
	if(waypointlist ==NULL){
		return WAYPOINT_OUTPUT_UPDATE_FAIL;
	}
	latestmessage.waypoint_list = *waypointlist;
	latestmessage.waypoint_list_valid = true;
	return WAYPOINT_OUTPUT_UPDATE_SUCCESS;
}

//3.3
TxMessageResult_t CreateTxMessage(TxMessage_t *txmessage){
	if(txmessage ==NULL){
		return TX_MESSAGE_FAIL;
	}
	if (output_data_ready == false) {
	    return TX_MESSAGE_FAIL;
	}
	*txmessage = latestmessage;
	return TX_MESSAGE_SUCCESS;
}
