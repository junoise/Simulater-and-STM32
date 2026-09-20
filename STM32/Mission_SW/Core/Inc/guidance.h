
#ifndef GUIDANCE_H_
#define GUIDANCE_H_

#include "app_types.h"

GuidanceResult_t CalculateGuidance(const AircraftState_t *aircraft_state,const Waypoint_t *waypoint,TargetCommand_t *target_command);

#endif
