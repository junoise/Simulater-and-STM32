#include "mission_manager.h"
#include "waypoint_manager.h"
#include "guidance.h"
#include <stddef.h>

//3.2
static InitializeResult_t ProcessInitialize(const MissionInput_t *mission_input);

//3.3
static NavigateResult_t ProcessNavigate(const AircraftState_t *aircraftstate);

//3.4
static MissionCompleteResult_t ProcessMissionComplete(void);

static bool target_command_valid = false;

static TargetCommand_t cur_target_command = { 0 };
static MissionState_t missionstate = MISSION_STATE_INITIALIZE;
MissionProcessResult_t ProcessMission(const MissionInput_t *mission_input,const SystemStatus_t *SystemStatus){
	if(mission_input==NULL ||SystemStatus==NULL){
		return MISSION_PROCESS_FAIL;
	}
	/* Phase 1: system status is observed only; it does not control missions. */

	switch (missionstate){

	case MISSION_STATE_INITIALIZE:

		if(ProcessInitialize(mission_input)==INITIALIZE_FAIL){
			return MISSION_PROCESS_FAIL;
		}

		break;
	case MISSION_STATE_NAVIGATE:

		 /* 새 목적지가 포함된 경우에만 Waypoint 재생성 */
		    if (mission_input->destination_valid == true) {
		        if (GenerateWaypointList(
		                &mission_input->aircraft_state,
		                &mission_input->destination)
		                != WAYPOINT_GENERATE_SUCCESS) {

		            return MISSION_PROCESS_FAIL;
		        }
		    }

		    /* 기존 또는 새로 생성한 Waypoint를 기준으로 항법 수행 */
		    if (ProcessNavigate(
		            &mission_input->aircraft_state) == NAVIGATE_FAIL) {

		        return MISSION_PROCESS_FAIL;
		    }
		    break;
	case MISSION_STATE_COMPLETE:
		ProcessMissionComplete();
		break;
	default:
		return MISSION_PROCESS_FAIL;
	}

	return MISSION_PROCESS_SUCCESS;
}

//3.2
static InitializeResult_t ProcessInitialize(const MissionInput_t *mission_input){

	if(mission_input->destination_valid==false){
		return INITIALIZE_WAIT;
	}
	if(mission_input->mission_start_command==0){
		return INITIALIZE_WAIT;
	}
	if(mission_input->mission_command_valid==false){
		return INITIALIZE_WAIT;
	}
	if(GenerateWaypointList(&mission_input->aircraft_state,&mission_input->destination)==WAYPOINT_GENERATE_FAIL){
		return INITIALIZE_FAIL;
	}
	missionstate = MISSION_STATE_NAVIGATE;
	return INITIALIZE_SUCCESS;
}

//3.3
static NavigateResult_t ProcessNavigate(const AircraftState_t *aircraftstate){
	Waypoint_t currentwaypoint = {0};

	switch(UpdateWaypointProgress(aircraftstate)){

	case WAYPOINT_CHECK_FAIL:
		return NAVIGATE_FAIL;


	case FINAL_WAYPOINT_REACHED:
		missionstate = MISSION_STATE_COMPLETE;
		return NAVIGATE_COMPLETE;

	case WAYPOINT_IN_PROGRESS:
		if(GetCurrentWaypoint(&currentwaypoint)==false){
			return NAVIGATE_FAIL;
		}
		if(CalculateGuidance(aircraftstate,
				&currentwaypoint,&cur_target_command)==GUIDANCE_CALCULATE_FAIL){
			return NAVIGATE_FAIL;
		}
		target_command_valid = true;
		return NAVIGATE_SUCCESS;

	default:
			return NAVIGATE_FAIL;
	}

}

//3.4
static MissionCompleteResult_t ProcessMissionComplete(void){
	return MISSION_COMPLETE_SUCCESS;
}

MissionState_t GetMissionState(void)
{
    return missionstate;
}

bool GetTargetCommand(TargetCommand_t *target_command)
{
    if (target_command == NULL) {
        return false;
    }

    /* 아직 정상적으로 계산한 목표값이 없는 경우 */
    if (target_command_valid == false) {
        return false;
    }

    *target_command = cur_target_command;

    return true;
}
