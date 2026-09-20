#include "waypoint_manager.h"
#include "navigation_math.h"
#include <stddef.h>
#include <math.h>

#define GENERATED_WAYPOINT_COUNT 5U

static uint8_t current_waypoint_index = 0U;
static WaypointList_t current_waypointlist = { 0 };

WaypointGenerateResult_t GenerateWaypointList(
		const AircraftState_t *aircraft_state, const Destination_t *destination) {
	uint8_t index;
	float fraction;

	if ((aircraft_state == NULL) || (destination == NULL)) {
		return WAYPOINT_GENERATE_FAIL;
	}
	current_waypoint_index = 0U;
	current_waypointlist.waypoint_count = 0U;

	for (index = 0U; index < GENERATED_WAYPOINT_COUNT; index++) {

		fraction = (float) (index + 1U) / (float) GENERATED_WAYPOINT_COUNT;

		if (NavigationMath_InterpolateGreatCircle(
				aircraft_state->current_latitude,
				aircraft_state->current_longitude,
				destination->destination_latitude,
				destination->destination_longitude, fraction,
				&current_waypointlist.waypoint[index].latitude,
				&current_waypointlist.waypoint[index].longitude) == false) {

			current_waypointlist.waypoint_count = 0U;
			return WAYPOINT_GENERATE_FAIL;
		}

		current_waypointlist.waypoint[index].altitude =
				aircraft_state->current_altitude
						+ ((destination->destination_altitude
								- aircraft_state->current_altitude) * fraction);
	}

	current_waypointlist.waypoint_count = GENERATED_WAYPOINT_COUNT;

	return WAYPOINT_GENERATE_SUCCESS;
}
//상세설계 3.1

bool GetCurrentWaypoint(Waypoint_t *waypoint) {

	if (waypoint == NULL) {
		return false;
	}
	if (current_waypointlist.waypoint_count == 0U) {
		return false;

	}
	if (current_waypoint_index >= current_waypointlist.waypoint_count) {
		return false;
	}
	*waypoint = current_waypointlist.waypoint[current_waypoint_index];

	return true;
}
//상세설계 3.2

bool GetWaypointList(WaypointList_t *waypointlist) {

	if (waypointlist == NULL) {
		return false;
	}
	if (current_waypointlist.waypoint_count == 0U) {
		return false;
	}
	*waypointlist = current_waypointlist;
	return true;
}
//상세설계 3.3

uint8_t GetCurrentWaypointIndex(void) {

	return current_waypoint_index;
}
//상세설계 3.4

WaypointProgressResult_t UpdateWaypointProgress(
		const AircraftState_t *aircraft_state) {

	float horizontal_distance;
	float altitude_error;
	const float WAYPOINT_REACHED_DISTANCE_M = 50.0f;
	const float WAYPOINT_REACHED_ALTITUDE_M = 10.0f; //임시 오차 범위
	if (aircraft_state == NULL) {
		return WAYPOINT_CHECK_FAIL;
	}
	if (current_waypointlist.waypoint_count == 0U) {
		return WAYPOINT_CHECK_FAIL;
	}
	if (current_waypoint_index >= current_waypointlist.waypoint_count) {
		return WAYPOINT_CHECK_FAIL;
	}

	const Waypoint_t *target_waypoint =
			&current_waypointlist.waypoint[current_waypoint_index];

	if (NavigationMath_CalculateDistance(aircraft_state->current_latitude,
			aircraft_state->current_longitude, target_waypoint->latitude,
			target_waypoint->longitude, &horizontal_distance) == false) {

		return WAYPOINT_CHECK_FAIL;
	} //거리계산 함수.

	altitude_error = fabsf(
			aircraft_state->current_altitude - target_waypoint->altitude);

	if ((horizontal_distance > WAYPOINT_REACHED_DISTANCE_M)
			|| (altitude_error > WAYPOINT_REACHED_ALTITUDE_M)) {
		return WAYPOINT_IN_PROGRESS;
	}

	if (current_waypoint_index == (current_waypointlist.waypoint_count - 1U)) {
		return FINAL_WAYPOINT_REACHED;
	}
	current_waypoint_index++;

	return WAYPOINT_IN_PROGRESS;
}
//상세설계 3.5
