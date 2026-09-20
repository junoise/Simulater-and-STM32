#include "guidance.h"
#include "navigation_math.h"
#include <stddef.h>

GuidanceResult_t CalculateGuidance(const AircraftState_t *aircraft_state,
		const Waypoint_t *waypoint, TargetCommand_t *target_command) {

	if (aircraft_state == NULL || waypoint == NULL || target_command == NULL) {
		return GUIDANCE_CALCULATE_FAIL;
	}

	if (NavigationMath_CalculateInitialBearing(aircraft_state->current_latitude,
			aircraft_state->current_longitude, waypoint->latitude,
			waypoint->longitude, &target_command->target_heading) == false) {
		return GUIDANCE_CALCULATE_FAIL;
	}
	target_command->target_altitude = waypoint->altitude;
	target_command->target_speed = 200.0f; //임시 순항값.
	return GUIDANCE_CALCULATE_SUCCESS;
}
