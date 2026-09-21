#include "data_manager.h"
#include <stddef.h>
#include <math.h>
#include "FreeRTOS.h"
#include "task.h"

static AircraftState_t latest_aircraft_state = { 0 };
static Destination_t latest_destination_state = { 0 };
static uint8_t latest_mission_start_command = 0U;
static DataStatus_t data_status = DATA_UNKNOWN;

static bool aircraft_valid = false;
static bool destination_valid = false;
static bool mission_command_valid = false;


AircraftValidationResult_t ValidateAircraftState(
		const AircraftState_t *aircraftstate) {

	if (aircraftstate == NULL) {
		return AIRCRAFT_FAIL;
	}
	if (!isfinite(aircraftstate->current_latitude)
			|| !isfinite(aircraftstate->current_longitude)
			|| !isfinite(aircraftstate->current_altitude)
			|| !isfinite(aircraftstate->current_heading)
			|| !isfinite(aircraftstate->current_speed)
			|| !isfinite(aircraftstate->current_fuel)) {
		return AIRCRAFT_FAIL;
	}

	if ((aircraftstate->current_latitude < -90.0f)
			|| (aircraftstate->current_latitude > 90.0f)) {
		return AIRCRAFT_FAIL;
	}

	if ((aircraftstate->current_longitude < -180.0f)
			|| (aircraftstate->current_longitude > 180.0f)) {
		return AIRCRAFT_FAIL;
	}

	if (aircraftstate->current_altitude < 0.0f) {
		return AIRCRAFT_FAIL;
	}

	if ((aircraftstate->current_heading < 0.0f)
			|| (aircraftstate->current_heading >= 360.0f)) {
		return AIRCRAFT_FAIL;
	}

	if (aircraftstate->current_speed < 0.0f) {
		return AIRCRAFT_FAIL;
	}
	if ((aircraftstate->current_fuel < 0.0f)
			|| (aircraftstate->current_fuel > 100.0f)) {
		return AIRCRAFT_FAIL;
	}

	return AIRCRAFT_SUCCESS;

}

DestinationValidationResult_t ValidateDestination(
		const Destination_t *destination) {

	if (destination == NULL) {
		return DESTINATION_FAIL;
	}
	if (!isfinite(destination->destination_latitude)
			|| !isfinite(destination->destination_longitude)
			|| !isfinite(destination->destination_altitude)) {
		return DESTINATION_FAIL;
	}

	if ((destination->destination_latitude < -90.0f)
			|| (destination->destination_latitude > 90.0f)) {
		return DESTINATION_FAIL;
	}

	if ((destination->destination_longitude < -180.0f)
			|| (destination->destination_longitude > 180.0f)) {
		return DESTINATION_FAIL;
	}

	if (destination->destination_altitude < 0.0f) {
		return DESTINATION_FAIL;
	}

	return DESTINATION_SUCCESS;

}

MissionCommandValidationResult_t ValidateMissionCommand(
		uint8_t mission_start_command) {

	if (mission_start_command == 0 || mission_start_command == 1) {
		return MISSION_COMMAND_SUCCESS;
	} else {
		return MISSION_COMMAND_FAIL;
	}
}

DataUpdateResult_t UpdateData(const RxMessage_t *rx_message) {

	AircraftValidationResult_t aircraft_result;

	DestinationValidationResult_t destination_result;

	MissionCommandValidationResult_t command_result;

	if (rx_message == NULL) {
		data_status = DATA_INVALID;
		return UPDATE_FAIL;
	}
	destination_valid = false;
	mission_command_valid = false;
	data_status = DATA_INVALID;


	/* 항공기 검사 → 상태 갱신 → 유효하면 저장 */
	aircraft_result = ValidateAircraftState(&rx_message->aircraft_state);
	taskENTER_CRITICAL();
	aircraft_valid = (aircraft_result == AIRCRAFT_SUCCESS);
	if (aircraft_valid) {
		latest_aircraft_state = rx_message->aircraft_state;
	}
	taskEXIT_CRITICAL();
	if (aircraft_result == AIRCRAFT_SUCCESS) {
		data_status = DATA_VALID;
	} else {
		data_status = DATA_INVALID;
		return UPDATE_FAIL;
	}

	/* 새 목적지가 있으면 검사 → 상태 갱신 → 유효하면 저장 */
	if (rx_message->destination_valid) {
		destination_result = ValidateDestination(&rx_message->destination);
		destination_valid = (destination_result == DESTINATION_SUCCESS);
		if (destination_valid) {
			latest_destination_state = rx_message->destination;
		}else{
			data_status = DATA_INVALID;
		}
	}
	/* 새 명령이 있으면 검사 → 상태 갱신 → 유효하면 저장 */
	if (rx_message->mission_command_valid) {
		command_result = ValidateMissionCommand(
				rx_message->mission_start_command);
		mission_command_valid = (command_result == MISSION_COMMAND_SUCCESS);
		if (mission_command_valid) {
			latest_mission_start_command = rx_message->mission_start_command;
		}else{
			data_status = DATA_INVALID;
		}
	}


	return UPDATE_SUCCESS;

}

MissionInputResult_t CreateMissionInput(MissionInput_t *missioninput) {
	if (missioninput == NULL) {
		return INPUT_FAIL;
	}

	if (aircraft_valid == false) {
		return INPUT_FAIL;
	}
	missioninput->aircraft_state = latest_aircraft_state;
	missioninput->destination = latest_destination_state;
	missioninput->destination_valid = destination_valid;
	missioninput->mission_start_command = latest_mission_start_command;
	missioninput->mission_command_valid = mission_command_valid;

	return INPUT_SUCCESS;
}

DataStatus_t GetDataStatus(void)
{
    return data_status;
}
bool GetCurrentFuel(float *fuel){
	if (fuel == NULL) {
	        return false;
	    }
	taskENTER_CRITICAL();
	bool valid = aircraft_valid;
	if (valid) {
		*fuel = latest_aircraft_state.current_fuel;
	}
	taskEXIT_CRITICAL();
	return valid;
}
