

#ifndef WAYPOINT_MANAGER_H_
#define WAYPOINT_MANAGER_H_

#include "app_types.h"

WaypointGenerateResult_t GenerateWaypointList(const AircraftState_t *aircraft_state,const Destination_t *destination);
//상세설계 3.1

bool GetCurrentWaypoint(Waypoint_t *waypoint);

bool GetWaypointList(WaypointList_t *waypointlist);

uint8_t GetCurrentWaypointIndex(void);

WaypointProgressResult_t UpdateWaypointProgress(const AircraftState_t *aircraftstate);

#endif
