
#ifndef APP_TYPES_H
#define APP_TYPES_H

#define MAX_WAYPOINT_COUNT 10U

#include <stdint.h>
#include <stdbool.h>

//Communication
typedef enum
{
    RX_SUCCESS = 0,
    RX_FAIL
} RxResult_t;

typedef enum
{
    PARSE_SUCCESS = 0,
    PARSE_FAIL
} ParseResult_t;

typedef enum
{
    PACKET_SUCCESS = 0,
    PACKET_FAIL
} PacketResult_t;

typedef enum
{
    TRANSMIT_SUCCESS = 0,
    TRANSMIT_FAIL
} TransmitResult_t;

typedef enum
{
    COMM_OK = 0,
    COMM_ERROR
} CommunicationStatus_t;

//Data Manager
typedef enum
{
    AIRCRAFT_SUCCESS = 0,
    AIRCRAFT_FAIL
} AircraftValidationResult_t;

typedef enum
{
    DESTINATION_SUCCESS = 0,
    DESTINATION_FAIL
} DestinationValidationResult_t;

typedef enum
{
    MISSION_COMMAND_SUCCESS = 0,
    MISSION_COMMAND_FAIL
} MissionCommandValidationResult_t;

typedef enum
{
    UPDATE_SUCCESS = 0,
    UPDATE_FAIL
} DataUpdateResult_t;

typedef enum
{
    INPUT_SUCCESS = 0,
    INPUT_FAIL
} MissionInputResult_t;

typedef enum
{
	DATA_VALID   = 0,
	DATA_INVALID = 1,
	DATA_UNKNOWN = 2

} DataStatus_t;

//System Monitor

typedef enum
{
    FUEL_NORMAL = 0,
    FUEL_LOW
} FuelStatus_t;

typedef enum
{
    COMM_STATUS_UPDATE_SUCCESS = 0,
    COMM_STATUS_UPDATE_FAIL
} CommStatusUpdateResult_t;

//Waypoint Manager

typedef enum
{
    WAYPOINT_GENERATE_SUCCESS = 0,
    WAYPOINT_GENERATE_FAIL
} WaypointGenerateResult_t;

typedef enum
{
    WAYPOINT_IN_PROGRESS = 0,
    FINAL_WAYPOINT_REACHED,
    WAYPOINT_CHECK_FAIL
} WaypointProgressResult_t;

//Guidance

typedef enum
{
    GUIDANCE_CALCULATE_SUCCESS = 0,
    GUIDANCE_CALCULATE_FAIL
} GuidanceResult_t;

//Mission Manager

typedef enum
{
	MISSION_STATE_INITIALIZE = 0,
	MISSION_STATE_NAVIGATE   = 1,
	MISSION_STATE_COMPLETE   = 2

} MissionState_t;

typedef enum
{
    MISSION_PROCESS_SUCCESS = 0,
    MISSION_PROCESS_FAIL
} MissionProcessResult_t;

typedef enum
{
    INITIALIZE_SUCCESS = 0,
    INITIALIZE_WAIT,
    INITIALIZE_FAIL
} InitializeResult_t;

typedef enum
{
    NAVIGATE_SUCCESS = 0,
    NAVIGATE_COMPLETE,
    NAVIGATE_FAIL
} NavigateResult_t;

typedef enum
{
    MISSION_COMPLETE_SUCCESS = 0
} MissionCompleteResult_t;

//OutputData Manager

typedef enum
{
    OUTPUT_UPDATE_SUCCESS = 0,
    OUTPUT_UPDATE_FAIL
} OutputUpdateResult_t;

typedef enum
{
    WAYPOINT_OUTPUT_UPDATE_SUCCESS = 0,
    WAYPOINT_OUTPUT_UPDATE_FAIL
} WaypointOutputUpdateResult_t;

typedef enum
{
    TX_MESSAGE_SUCCESS = 0,
    TX_MESSAGE_FAIL
} TxMessageResult_t;

//구조체

typedef struct
{
	float current_latitude;
	float current_longitude;
	float current_altitude;
	float current_heading;
	float current_speed;
	float current_fuel;

}AircraftState_t;

typedef struct
{
	float destination_latitude;
	float destination_longitude;
	float destination_altitude;

}Destination_t;

typedef struct
{
	float latitude;
	float longitude;
	float altitude;

}Waypoint_t;

typedef struct
{
	Waypoint_t waypoint[MAX_WAYPOINT_COUNT];
	uint8_t waypoint_count;

}WaypointList_t;

typedef struct
{
	float target_heading;
	float target_altitude;
	float target_speed;

}TargetCommand_t;

typedef struct
{
	AircraftState_t aircraft_state;
	Destination_t destination;
	bool destination_valid;
	uint8_t mission_start_command;
	bool mission_command_valid;

}MissionInput_t;

typedef struct
{
	CommunicationStatus_t communication_status;
	FuelStatus_t fuel_status;

}SystemStatus_t;

typedef struct
{
	float target_altitude;
	float target_heading;
	float target_speed;
	uint8_t current_waypoint_index;
	MissionState_t mission_state;
	DataStatus_t data_status;
}OutputData_t;

typedef struct
{
	AircraftState_t aircraft_state;
	Destination_t destination;
	bool destination_valid;
	uint8_t mission_start_command;
	bool mission_command_valid;

}RxMessage_t;

typedef struct
{
	OutputData_t output_data;
	WaypointList_t waypoint_list;
	bool waypoint_list_valid;

}TxMessage_t;


#endif
