#include "output_data_manager.h"
#include "data_manager.h"
#include <stddef.h>
#include "FreeRTOS.h"
#include "task.h"
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
	if(waypointlist ==NULL || waypointlist->waypoint_count == 0U
			|| waypointlist->waypoint_count > MAX_WAYPOINT_COUNT){
		return WAYPOINT_OUTPUT_UPDATE_FAIL;
	}
	/* Mission Task owns list contents. Re-publishing the same list is a no-op. */
	bool unchanged = latestmessage.waypoint_version != 0U
			&& latestmessage.waypoint_list.waypoint_count == waypointlist->waypoint_count;
	for (uint8_t i = 0U; unchanged && i < waypointlist->waypoint_count; ++i) {
		const Waypoint_t *old = &latestmessage.waypoint_list.waypoint[i];
		const Waypoint_t *next = &waypointlist->waypoint[i];
		unchanged = old->latitude == next->latitude
				&& old->longitude == next->longitude && old->altitude == next->altitude;
	}
	if (unchanged) {
		return WAYPOINT_OUTPUT_UPDATE_SUCCESS;
	}
	/* Synchronize with TX acknowledgement; version zero is reserved. */
	taskENTER_CRITICAL();
	latestmessage.waypoint_list = *waypointlist;
	++latestmessage.waypoint_version;
	if (latestmessage.waypoint_version == 0U) {
		++latestmessage.waypoint_version;
	}
	latestmessage.waypoint_list_valid = true;
	taskEXIT_CRITICAL();
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
	/* Keep pending in every overwrite until UART completion is acknowledged. */
	taskENTER_CRITICAL();
	*txmessage = latestmessage;
	taskEXIT_CRITICAL();
	return TX_MESSAGE_SUCCESS;
}

void ConfirmWaypointTransmitted(uint64_t version)
{
	taskENTER_CRITICAL();
	if (version != 0U && version == latestmessage.waypoint_version) {
		latestmessage.waypoint_list_valid = false;
	}
	taskEXIT_CRITICAL();
}
