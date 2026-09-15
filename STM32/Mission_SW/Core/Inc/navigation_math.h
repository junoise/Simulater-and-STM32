
#ifndef INC_NAVIGATION_MATH_H_
#define INC_NAVIGATION_MATH_H_

#include <stdbool.h>

bool NavigationMath_CalculateDistance(
        float start_latitude,
        float start_longitude,
        float end_latitude,
        float end_longitude,
        float *distance_m);

bool NavigationMath_InterpolateGreatCircle(
        float start_latitude,
        float start_longitude,
        float end_latitude,
        float end_longitude,
        float fraction,
        float *result_latitude,
        float *result_longitude);


#endif
