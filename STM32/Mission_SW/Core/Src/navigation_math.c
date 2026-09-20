#include "navigation_math.h"

#include <stddef.h>
#include <math.h>

#define EARTH_RADIUS_M    6371000.0f
#define DEG_TO_RAD        0.017453292519943295f
#define RAD_TO_DEG        57.29577951308232f
#define SLERP_EPSILON     0.000001f

static bool IsCoordinateValid(float latitude, float longitude);

static float ConvertDegreesToRadians(float degrees);

static float ConvertRadiansToDegrees(float radians);

static float CalculateCentralAngle(float start_latitude, float start_longitude,
		float end_latitude, float end_longitude);

static bool IsCoordinateValid(float latitude, float longitude) {
	if ((!isfinite(latitude)) || (!isfinite(longitude))) {
		return false;
	}

	if ((latitude < -90.0f) || (latitude > 90.0f)) {
		return false;
	}

	if ((longitude < -180.0f) || (longitude > 180.0f)) {
		return false;
	}

	return true;
}

static float ConvertDegreesToRadians(float degrees) {
	return degrees * DEG_TO_RAD;
}

static float ConvertRadiansToDegrees(float radians) {
	return radians * RAD_TO_DEG;
}

/*
 * Haversine 공식으로 두 좌표 사이의 중심각을 계산한다.
 * 반환 단위는 radian이다.
 */
static float CalculateCentralAngle(float start_latitude, float start_longitude,
		float end_latitude, float end_longitude) {
	float start_latitude_rad;
	float start_longitude_rad;
	float end_latitude_rad;
	float end_longitude_rad;

	float latitude_difference;
	float longitude_difference;

	float latitude_sine;
	float longitude_sine;

	float haversine_value;

	start_latitude_rad = ConvertDegreesToRadians(start_latitude);

	start_longitude_rad = ConvertDegreesToRadians(start_longitude);

	end_latitude_rad = ConvertDegreesToRadians(end_latitude);

	end_longitude_rad = ConvertDegreesToRadians(end_longitude);

	latitude_difference = end_latitude_rad - start_latitude_rad;

	longitude_difference = end_longitude_rad - start_longitude_rad;

	latitude_sine = sinf(latitude_difference * 0.5f);

	longitude_sine = sinf(longitude_difference * 0.5f);

	haversine_value = (latitude_sine * latitude_sine)
			+ (cosf(start_latitude_rad) * cosf(end_latitude_rad)
					* longitude_sine * longitude_sine);

	/*
	 * 부동소수점 오차 때문에 haversine_value가
	 * 0~1 범위를 미세하게 벗어나는 것을 방지한다.
	 */
	if (haversine_value < 0.0f) {
		haversine_value = 0.0f;
	} else if (haversine_value > 1.0f) {
		haversine_value = 1.0f;
	}

	return 2.0f * atan2f(sqrtf(haversine_value), sqrtf(1.0f - haversine_value));
}

/*
 * 두 좌표 사이의 대권거리를 계산한다.
 * 결과 단위는 meter이다.
 */
bool NavigationMath_CalculateDistance(float start_latitude,
		float start_longitude, float end_latitude, float end_longitude,
		float *distance_m) {
	float central_angle;

	if (distance_m == NULL) {
		return false;
	}

	if ((!IsCoordinateValid(start_latitude, start_longitude))
			|| (!IsCoordinateValid(end_latitude, end_longitude))) {

		return false;
	}

	central_angle = CalculateCentralAngle(start_latitude, start_longitude,
			end_latitude, end_longitude);

	*distance_m = EARTH_RADIUS_M * central_angle;

	if (!isfinite(*distance_m)) {
		return false;
	}

	return true;
}

/*
 * 대권항로에서 fraction에 해당하는 좌표를 계산한다.
 *
 * fraction = 0.0: 시작점
 * fraction = 0.5: 중간 지점
 * fraction = 1.0: 목적지
 */
bool NavigationMath_InterpolateGreatCircle(float start_latitude,
		float start_longitude, float end_latitude, float end_longitude,
		float fraction, float *result_latitude, float *result_longitude) {
	float start_latitude_rad;
	float start_longitude_rad;
	float end_latitude_rad;
	float end_longitude_rad;

	float start_x;
	float start_y;
	float start_z;

	float end_x;
	float end_y;
	float end_z;

	float result_x;
	float result_y;
	float result_z;

	float central_angle;
	float sine_central_angle;
	float start_weight;
	float end_weight;
	float vector_length;

	if ((result_latitude == NULL) || (result_longitude == NULL)) {
		return false;
	}

	if ((!IsCoordinateValid(start_latitude, start_longitude))
			|| (!IsCoordinateValid(end_latitude, end_longitude))) {

		return false;
	}

	if ((!isfinite(fraction)) || (fraction < 0.0f) || (fraction > 1.0f)) {
		return false;
	}

	/*
	 * 양 끝점은 불필요한 삼각함수 계산 없이
	 * 입력 좌표를 그대로 반환한다.
	 */
	if (fraction == 0.0f) {
		*result_latitude = start_latitude;
		*result_longitude = start_longitude;
		return true;
	}

	if (fraction == 1.0f) {
		*result_latitude = end_latitude;
		*result_longitude = end_longitude;
		return true;
	}

	start_latitude_rad = ConvertDegreesToRadians(start_latitude);

	start_longitude_rad = ConvertDegreesToRadians(start_longitude);

	end_latitude_rad = ConvertDegreesToRadians(end_latitude);

	end_longitude_rad = ConvertDegreesToRadians(end_longitude);

	/*
	 * 위도·경도를 3차원 단위벡터로 변환한다.
	 */
	start_x = cosf(start_latitude_rad) * cosf(start_longitude_rad);

	start_y = cosf(start_latitude_rad) * sinf(start_longitude_rad);

	start_z = sinf(start_latitude_rad);

	end_x = cosf(end_latitude_rad) * cosf(end_longitude_rad);

	end_y = cosf(end_latitude_rad) * sinf(end_longitude_rad);

	end_z = sinf(end_latitude_rad);

	central_angle = CalculateCentralAngle(start_latitude, start_longitude,
			end_latitude, end_longitude);

	sine_central_angle = sinf(central_angle);

	/*
	 * 두 좌표가 거의 같으면 SLERP의 분모가
	 * 0에 가까워지므로 일반 선형 가중치를 사용한다.
	 */
	if (central_angle < SLERP_EPSILON) {
		start_weight = 1.0f - fraction;
		end_weight = fraction;
	}
	/*
	 * 두 좌표가 지구 정반대에 있으면 최단 대권항로가
	 * 하나로 결정되지 않으므로 실패 처리한다.
	 */
	else if (fabsf(sine_central_angle) < SLERP_EPSILON) {
		return false;
	} else {
		start_weight = sinf((1.0f - fraction) * central_angle)
				/ sine_central_angle;

		end_weight = sinf(fraction * central_angle) / sine_central_angle;
	}

	result_x = (start_weight * start_x) + (end_weight * end_x);

	result_y = (start_weight * start_y) + (end_weight * end_y);

	result_z = (start_weight * start_z) + (end_weight * end_z);

	/*
	 * 계산 오차를 제거하기 위해 단위벡터로 정규화한다.
	 */
	vector_length = sqrtf(
			(result_x * result_x) + (result_y * result_y)
					+ (result_z * result_z));

	if (vector_length < SLERP_EPSILON) {
		return false;
	}

	result_x /= vector_length;
	result_y /= vector_length;
	result_z /= vector_length;

	/*
	 * 3차원 단위벡터를 다시 위도·경도로 변환한다.
	 */
	*result_latitude = ConvertRadiansToDegrees(
			atan2f(result_z,
					sqrtf((result_x * result_x) + (result_y * result_y))));

	*result_longitude = ConvertRadiansToDegrees(atan2f(result_y, result_x));

	if ((!isfinite(*result_latitude)) || (!isfinite(*result_longitude))) {
		return false;
	}

	return true;
}

bool NavigationMath_CalculateInitialBearing(
        float start_latitude,
        float start_longitude,
        float end_latitude,
        float end_longitude,
        float *bearing_degree)
{
    float start_latitude_rad;
    float end_latitude_rad;
    float longitude_difference_rad;

    float east_component;
    float north_component;
    float direction_magnitude;
    float bearing_rad;
    float result_degree;

    /* 방향 벡터가 거의 0인지 판단하는 수치 계산용 기준 */
    const float DIRECTION_EPSILON = 1.0e-7f;

    if (bearing_degree == NULL) {
        return false;
    }

    if ((!IsCoordinateValid(start_latitude, start_longitude))
            || (!IsCoordinateValid(end_latitude, end_longitude))) {
        return false;
    }

    start_latitude_rad =
            ConvertDegreesToRadians(start_latitude);

    end_latitude_rad =
            ConvertDegreesToRadians(end_latitude);

    longitude_difference_rad =
            ConvertDegreesToRadians(
                    end_longitude - start_longitude);

    /* 출발점이 극점에 가까우면 북쪽 기준 방향이 불안정하다. */
    if (fabsf(cosf(start_latitude_rad)) < DIRECTION_EPSILON) {
        return false;
    }

    east_component =
            sinf(longitude_difference_rad)
            * cosf(end_latitude_rad);

    north_component =
            cosf(start_latitude_rad) * sinf(end_latitude_rad)
            - sinf(start_latitude_rad) * cosf(end_latitude_rad)
                    * cosf(longitude_difference_rad);

    direction_magnitude = sqrtf(
            east_component * east_component
            + north_component * north_component);

    /*
     * 두 위치가 같거나 대척점에 가까우면
     * 방위각을 안정적으로 결정할 수 없다.
     */
    if ((!isfinite(direction_magnitude))
            || (direction_magnitude < DIRECTION_EPSILON)) {
        return false;
    }

    bearing_rad = atan2f(east_component, north_component);

    result_degree = ConvertRadiansToDegrees(bearing_rad);

    if (!isfinite(result_degree)) {
        return false;
    }

    if (result_degree < 0.0f) {
        result_degree += 360.0f;
    }

    /* float 반올림으로 360도가 되는 경우도 0도로 정규화 */
    if (result_degree >= 360.0f) {
        result_degree = 0.0f;
    }

    *bearing_degree = result_degree;

    return true;
}
