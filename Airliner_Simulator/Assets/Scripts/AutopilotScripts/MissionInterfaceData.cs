using System;
using UnityEngine;

public enum MissionStateCode : byte
{
    INITIALIZE = 0,
    NAVIGATE = 1,
    MISSION_COMPLETE = 2
}

public enum DataStatusCode : byte
{
    UNKNOWN = 0,
    VALID = 1,
    INVALID = 2
}

[Serializable]
public struct UnityAircraftState
{
    public float current_latitude;
    public float current_longitude;
    public float current_altitude;
    public float current_heading;
    public float current_speed;
    public float current_fuel;
}

[Serializable]
public struct UnityMissionRequest
{
    public float destination_latitude;
    public float destination_longitude;
    public float destination_altitude;
    public byte mission_start_command;
}

[Serializable]
public struct MissionCommand
{
    public float target_altitude;
    public float target_heading;
    public float target_speed;
    public byte current_waypoint_index;
    public byte mission_state;
    public byte data_status;
}

[Serializable]
public struct MissionWaypoint
{
    public float waypoint_latitude;
    public float waypoint_longitude;
    public float waypoint_altitude;
}

public static class MissionInterfaceRules
{
    public static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static bool PositionValid(float latitude, float longitude)
    {
        return Finite(latitude) &&
               Finite(longitude) &&
               latitude >= -90f && latitude <= 90f &&
               longitude >= -180f && longitude <= 180f;
    }

    public static bool StateValid(UnityAircraftState state)
    {
        return PositionValid(
                   state.current_latitude,
                   state.current_longitude
               ) &&
               Finite(state.current_altitude) &&
               Finite(state.current_heading) &&
               state.current_heading >= 0f &&
               state.current_heading < 360f &&
               Finite(state.current_speed) &&
               state.current_speed >= 0f &&
               Finite(state.current_fuel) &&
               state.current_fuel >= 0f &&
               state.current_fuel <= 100f;
    }

    public static bool CommandValid(MissionCommand command)
    {
        return Finite(command.target_altitude) &&
               Finite(command.target_heading) &&
               command.target_heading >= 0f &&
               command.target_heading < 360f &&
               Finite(command.target_speed) &&
               command.target_speed >= 0f &&
               command.mission_state <= 2 &&
               command.data_status <= 2;
    }

    public static bool WaypointValid(MissionWaypoint waypoint)
    {
        return PositionValid(
                   waypoint.waypoint_latitude,
                   waypoint.waypoint_longitude
               ) &&
               Finite(waypoint.waypoint_altitude);
    }

    public static double DistanceMeters(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2
    )
    {
        const double radius = 6371000.0;
        const double toRad = Math.PI / 180.0;

        double lat1 = latitude1 * toRad;
        double lat2 = latitude2 * toRad;
        double dLat = (latitude2 - latitude1) * toRad;
        double dLon = (longitude2 - longitude1) * toRad;

        double a =
            Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5) +
            Math.Cos(lat1) * Math.Cos(lat2) *
            Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);

        a = Math.Max(0.0, Math.Min(1.0, a));

        return radius * 2.0 *
               Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }

    public static float BearingDegrees(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2
    )
    {
        const double toRad = Math.PI / 180.0;

        double lat1 = latitude1 * toRad;
        double lat2 = latitude2 * toRad;
        double dLon = (longitude2 - longitude1) * toRad;

        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x =
            Math.Cos(lat1) * Math.Sin(lat2) -
            Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        return Mathf.Repeat(
            (float)(Math.Atan2(y, x) / toRad),
            360f
        );
    }

    public static MissionWaypoint PointAhead(
        UnityAircraftState state,
        float distanceMeters
    )
    {
        const double radius = 6371000.0;
        const double toRad = Math.PI / 180.0;

        double latitude = state.current_latitude * toRad;
        double longitude = state.current_longitude * toRad;
        double bearing = state.current_heading * toRad;
        double angularDistance = distanceMeters / radius;

        double sinLatitude2 =
            Math.Sin(latitude) * Math.Cos(angularDistance) +
            Math.Cos(latitude) * Math.Sin(angularDistance) *
            Math.Cos(bearing);

        double latitude2 = Math.Asin(
            Math.Max(-1.0, Math.Min(1.0, sinLatitude2))
        );

        double longitude2 = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) *
            Math.Cos(latitude),
            Math.Cos(angularDistance) -
            Math.Sin(latitude) * Math.Sin(latitude2)
        );

        double longitudeDegrees = longitude2 / toRad;
        longitudeDegrees =
            ((longitudeDegrees + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

        return new MissionWaypoint
        {
            waypoint_latitude = (float)(latitude2 / toRad),
            waypoint_longitude = (float)longitudeDegrees,
            waypoint_altitude = state.current_altitude
        };
    }
}