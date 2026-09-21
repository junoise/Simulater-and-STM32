#include "communication.h"
#include <stddef.h>
#include <string.h>
#include <float.h>
#include "main.h"
#include "FreeRTOS.h"
#include "task.h"
extern UART_HandleTypeDef huart2;

static uint8_t uart_rx_byte = 0U;
static TaskHandle_t rx_task_handle = NULL;

#define RX_BUFFER_SIZE 256U
static uint8_t rx_buffer[RX_BUFFER_SIZE];
static volatile uint16_t rx_write_index = 0U;
static volatile uint16_t rx_read_index = 0U;  //원형 버퍼 설정.

static volatile bool rx_buffer_overflow = false;
static volatile bool rx_rearm_failed = false;
static volatile bool rx_uart_error = false;
static volatile bool current_received = false;
static volatile uint32_t last_current_time = 0U;

#define COMM_TIMEOUT_MS 250U
#define TX_TIMEOUT_MS 100U
#define WAYPOINT_COUNT_ON_WIRE 5U
#define WAYPOINT_PAYLOAD_SIZE 61U
#define OUTPUT_PAYLOAD_SIZE 15U

_Static_assert(sizeof(float) == 4U && FLT_RADIX == 2 &&
               FLT_MANT_DIG == 24 && FLT_MAX_EXP == 128,
               "Wire protocol requires IEEE 754 binary32");
_Static_assert(MAX_WAYPOINT_COUNT >= WAYPOINT_COUNT_ON_WIRE,
               "Waypoint storage is too small");

/* Only the RX Task accesses pending_destination and pending_command. */
static Destination_t pending_destination;
static uint8_t pending_command;
static bool destination_pending = false;

/* Only the TX Task accesses these buffers. No raw C structs go on the wire. */
static uint8_t tx_waypoint_packet[68];
static uint8_t tx_output_packet[22];
static bool tx_packet_ready = false;
static bool tx_waypoint_pending = false; //rx 오류처리

static uint8_t header_buffer[3] = { 0U };
static uint8_t payload_buffer[24] = { 0U };
static uint8_t crc_buffer[2] = { 0U };
static uint16_t receive_num = 0U;
static uint16_t payload_long = 0U;
static uint32_t receive_time = 0U; //패킷조립 내부변수

static bool rx_packet_ready = false; // 패킷이 완성
static bool rx_packet_error = false; // 패킷 처리중 오류

#define RX_PACKET_TIMEOUT_MS 100U


typedef enum {
	WAIT_AA, WAIT_55, READ_HEADER, READ_PAYLOAD, READ_CRC

} RX_Status;

static RX_Status rx_status = WAIT_AA;

static bool PushRxByte(uint8_t data) //버퍼함수
{
	uint16_t next_index = (uint16_t) ((rx_write_index + 1U) % RX_BUFFER_SIZE);

	if (next_index == rx_read_index) {
		return false; //버퍼가 다참
	}
	rx_buffer[rx_write_index] = data;
	rx_write_index = next_index;

	return true;
}
void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart)
{
    BaseType_t higher_priority_task_woken = pdFALSE;

    if (huart->Instance != USART2) {
        return;
    }

    // 1. 받은 바이트를 원형 버퍼에 저장
    if (!rx_buffer_overflow && !rx_uart_error &&
        PushRxByte(uart_rx_byte) == false) {
        rx_buffer_overflow = true;
    }

    // 2. 다음 한 바이트 수신 준비
    if (HAL_UART_Receive_IT(huart, &uart_rx_byte, 1U) != HAL_OK) {
        rx_rearm_failed = true;
    }

    // 3. Comm RX Task에 데이터 또는 오류 발생 알림
    if (rx_task_handle != NULL) {
        vTaskNotifyGiveFromISR(
                rx_task_handle,
                &higher_priority_task_woken);
    }

    /* 필요하면 인터럽트 종료 직후 깨어난 Task 실행 */
    portYIELD_FROM_ISR(higher_priority_task_woken);
}
static bool PopRxByte(uint8_t *data)
{
    if (data == NULL) {
        return false;
    }
    bool available = false;
    taskENTER_CRITICAL();
    if (rx_read_index != rx_write_index) {
        *data = rx_buffer[rx_read_index];
        rx_read_index = (uint16_t)((rx_read_index + 1U) % RX_BUFFER_SIZE);
        available = true;
    }
    taskEXIT_CRITICAL();
    return available;
}

/* HAL calls this for parity/framing/noise/overrun errors. Recover in Task. */
void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart)
{
    if (huart->Instance != USART2) {
        return;
    }
    BaseType_t task_woken = pdFALSE;
    rx_uart_error = true;
    if (rx_task_handle != NULL) {
        vTaskNotifyGiveFromISR(rx_task_handle, &task_woken);
    }
    portYIELD_FROM_ISR(task_woken);
}

static void ResetRxPacket(void) {
	rx_status = WAIT_AA;
	receive_num = 0U;
	payload_long = 0U;
	receive_time = 0U;
}
static uint16_t UpdateCrc16(uint16_t crc, uint8_t data) {
	/* 입력 바이트를 CRC 상위 8비트에 XOR */
	crc ^= (uint16_t) ((uint16_t) data << 8U);

	for (uint8_t bit = 0U; bit < 8U; bit++) {
		if ((crc & 0x8000U) != 0U) {
			crc = (uint16_t) ((crc << 1U) ^ 0x1021U);
		} else {
			crc = (uint16_t) (crc << 1U);
		}
	}

	return crc;
} //crc 함수
static uint16_t CalculateRxCrc16(void) {
	uint16_t crc = 0xFFFFU;

	for (uint16_t index = 0U; index < 3U; index++) {
		crc = UpdateCrc16(crc, header_buffer[index]);
	}

	for (uint16_t index = 0U; index < payload_long; index++) {
		crc = UpdateCrc16(crc, payload_buffer[index]);
	}

	return crc;
} //crc 함수

static void ProcessRxByte(uint8_t data)
{

    if (rx_packet_ready || rx_packet_error) {
        return;
    }

    switch (rx_status) {
    case WAIT_AA:
        if (data == 0xAAU) {
            receive_time = HAL_GetTick();
            rx_status = WAIT_55;
        }
        break;

    case WAIT_55:
        if (data == 0x55U) {
            receive_num = 0U;
            rx_status = READ_HEADER;
        } else if (data != 0xAAU) {
            ResetRxPacket();
        }
        break;

    case READ_HEADER:
        header_buffer[receive_num] = data;
        receive_num++;

        if (receive_num == 3U) {
            payload_long = (uint16_t)(
                    header_buffer[1]
                    + (uint16_t)header_buffer[2] * 256U);

            if ((header_buffer[0] == 0x01U && payload_long == 13U) ||
                (header_buffer[0] == 0x02U && payload_long == 24U)) {
                receive_num = 0U;
                rx_status = READ_PAYLOAD;
            } else {
                ResetRxPacket();
                rx_packet_error = true;
            }
        }
        break;

    case READ_PAYLOAD:
        payload_buffer[receive_num] = data;
        receive_num++;

        if (receive_num == payload_long) {
            receive_num = 0U;
            rx_status = READ_CRC;
        }
        break;

    case READ_CRC:
        crc_buffer[receive_num] = data;
        receive_num++;

        if (receive_num == 2U) {
            uint16_t received_crc = (uint16_t)(
                    crc_buffer[0]
                    + (uint16_t)crc_buffer[1] * 256U);

            if (CalculateRxCrc16() == received_crc) {
                /* Parse()가 사용할 헤더와 payload 길이를 보존 */
                rx_packet_ready = true;
                if (header_buffer[0] == 0x02U) {
                    taskENTER_CRITICAL();
                    last_current_time = HAL_GetTick();
                    current_received = true;
                    taskEXIT_CRITICAL();
                }
            } else {
                ResetRxPacket();
                rx_packet_error = true;
            }
        }
        break;

    default:
        ResetRxPacket();
        rx_packet_error = true;
        break;
    }
}

static void CheckRxTimeout(void)
{
    // 완성된 패킷 또는 이미 발생한 오류는 그대로 유지
    if (rx_packet_ready || rx_packet_error) {
        return;
    }

    // 아직 시작 표시를 받지 않았다면 제한시간 없음
    if (rx_status == WAIT_AA) {
        return;
    }

    uint32_t elapsed = (uint32_t)(HAL_GetTick() - receive_time);

    if (elapsed >= RX_PACKET_TIMEOUT_MS) {
        ResetRxPacket();
        rx_packet_error = true;
    }
}
static void RecoverRx(void)
{
    HAL_StatusTypeDef abort_result;
    HAL_StatusTypeDef start_result = HAL_ERROR;

    // 인터럽트가 버퍼를 갱신하지 못하도록 잠시 보호
    taskENTER_CRITICAL();

    abort_result = HAL_UART_AbortReceive(&huart2);

    // 손상 가능성이 있는 수신 데이터 폐기
    rx_read_index = 0U;
    rx_write_index = 0U;

    ResetRxPacket();
    rx_packet_ready = false;
    rx_packet_error = false;
    rx_buffer_overflow = false;
    rx_uart_error = false;

    if (abort_result == HAL_OK) {
        __HAL_UART_CLEAR_OREFLAG(&huart2);
        start_result =
                HAL_UART_Receive_IT(&huart2, &uart_rx_byte, 1U);
    }

    rx_rearm_failed = (start_result != HAL_OK);

    taskEXIT_CRITICAL();
}
RxResult_t StartReceive(void)
{
    TaskHandle_t caller = xTaskGetCurrentTaskHandle();
    if (rx_task_handle != NULL) {
        return (rx_task_handle == caller && !rx_rearm_failed) ?
                RX_SUCCESS : RX_FAIL;
    }
    rx_task_handle = caller;
    RecoverRx();
    if (rx_rearm_failed) {
        rx_task_handle = NULL;
        return RX_FAIL;
    }
    return RX_SUCCESS;
}
RxResult_t Receive(void)
{
    uint8_t data;
    if (rx_task_handle == NULL ||
        rx_task_handle != xTaskGetCurrentTaskHandle()) {
        return RX_FAIL;
    }
    for (;;) {
        // 1. 수신 오류 복구
        if (rx_buffer_overflow || rx_rearm_failed || rx_uart_error) {
            RecoverRx();

            // 복구 실패 시 반복 호출로 CPU를 점유하지 않도록 대기
            if (rx_rearm_failed) {
                vTaskDelay(1U);
            }

            return RX_FAIL;
        }

        // 2. 패킷 수신 제한시간 확인
        CheckRxTimeout();

        if (rx_packet_error) {
            ResetRxPacket();
            rx_packet_error = false;
            return RX_FAIL;
        }

        // Parse()가 읽을 버퍼와 길이는 보존
        if (rx_packet_ready) {
            return RX_SUCCESS;
        }

        // 3. 바이트 하나 처리 후 처음부터 결과 확인 */
        if (PopRxByte(&data)) {
            ProcessRxByte(data);
            continue;
        }

        /* 4. 버퍼가 비었으므로 알림 대기 */
        TickType_t wait_ticks = portMAX_DELAY;

        if (rx_status != WAIT_AA) {
            uint32_t elapsed =
                    (uint32_t)(HAL_GetTick() - receive_time);

            if (elapsed >= RX_PACKET_TIMEOUT_MS) {
                continue; /* 위의 CheckRxTimeout()에서 처리 */
            }

            uint32_t remaining_ms =
                    RX_PACKET_TIMEOUT_MS - elapsed;

            /* ms를 RTOS tick으로 변환하고 올림 처리 */
            wait_ticks = (TickType_t)(
                    ((uint64_t)remaining_ms * configTICK_RATE_HZ
                     + 999U) / 1000U);
        }

        /*
         * 알림이 오거나 제한시간이 지나면 다시 검사.
         * 알림 개수와 관계없이 실제 데이터는 원형 버퍼에서 읽음.
         */
        (void)ulTaskNotifyTake(pdTRUE, wait_ticks);
    }
}

/* Explicit little-endian conversion: no alignment or pointer-aliasing casts. */
static float ReadFloatLE(const uint8_t *bytes)
{
    uint32_t bits = (uint32_t)bytes[0] |
                    ((uint32_t)bytes[1] << 8U) |
                    ((uint32_t)bytes[2] << 16U) |
                    ((uint32_t)bytes[3] << 24U);
    float value;
    memcpy(&value, &bits, sizeof(value));
    return value;
}

static void WriteFloatLE(uint8_t *bytes, float value)
{
    uint32_t bits;
    memcpy(&bits, &value, sizeof(bits));
    bytes[0] = (uint8_t)bits;
    bytes[1] = (uint8_t)(bits >> 8U);
    bytes[2] = (uint8_t)(bits >> 16U);
    bytes[3] = (uint8_t)(bits >> 24U);
}

ParseResult_t Parse(RxMessage_t *rxmessage)
{
    if (rxmessage == NULL || !rx_packet_ready) {
        return PARSE_FAIL;
    }

    ParseResult_t result = PARSE_FAIL;
    if (header_buffer[0] == 0x01U && payload_long == 13U) {
        pending_destination.destination_latitude = ReadFloatLE(&payload_buffer[0]);
        pending_destination.destination_longitude = ReadFloatLE(&payload_buffer[4]);
        pending_destination.destination_altitude = ReadFloatLE(&payload_buffer[8]);
        pending_command = payload_buffer[12];
        destination_pending = true;
        /* No Current yet: leave the caller's output untouched. */
        result = PARSE_WAIT;
    } else if (header_buffer[0] == 0x02U && payload_long == 24U) {
        RxMessage_t message = {0};
        message.aircraft_state.current_latitude = ReadFloatLE(&payload_buffer[0]);
        message.aircraft_state.current_longitude = ReadFloatLE(&payload_buffer[4]);
        message.aircraft_state.current_altitude = ReadFloatLE(&payload_buffer[8]);
        message.aircraft_state.current_heading = ReadFloatLE(&payload_buffer[12]);
        message.aircraft_state.current_speed = ReadFloatLE(&payload_buffer[16]);
        message.aircraft_state.current_fuel = ReadFloatLE(&payload_buffer[20]);
        if (destination_pending) {
            message.destination = pending_destination;
            message.mission_start_command = pending_command;
            message.destination_valid = true;
            message.mission_command_valid = true;
        }
        *rxmessage = message;
        result = PARSE_SUCCESS;
    }

    /* Release only packet assembly, not bytes queued for subsequent packets. */
    rx_packet_ready = false;
    ResetRxPacket();
    return result;
}

void ConfirmRxMessageDelivered(void)
{
    /* Single RX Task calls this synchronously after QueuePut succeeds. */
    destination_pending = false;
}

CommunicationStatus_t GetCommunicationStatus(void)
{
    uint32_t last;
    bool seen;
    taskENTER_CRITICAL();
    last = last_current_time;
    seen = current_received;
    taskEXIT_CRITICAL();

    return (seen && (uint32_t)(HAL_GetTick() - last) < COMM_TIMEOUT_MS) ?
            COMM_OK : COMM_ERROR;
}

static void BeginTxPacket(uint8_t *packet, uint8_t type, uint16_t length)
{
    packet[0] = 0xAAU;
    packet[1] = 0x55U;
    packet[2] = type;
    packet[3] = (uint8_t)length;
    packet[4] = (uint8_t)(length >> 8U);
}

static void FinishTxPacket(uint8_t *packet, uint16_t payload_length)
{
    uint16_t crc = 0xFFFFU;
    uint16_t crc_offset = (uint16_t)(5U + payload_length);
    for (uint16_t i = 2U; i < crc_offset; ++i) {
        crc = UpdateCrc16(crc, packet[i]);
    }
    packet[crc_offset] = (uint8_t)crc;
    packet[crc_offset + 1U] = (uint8_t)(crc >> 8U);
}

PacketResult_t CreateTxPacket(const TxMessage_t *txmessage)
{
    /* A failed build must never leave an older packet eligible for sending. */
    tx_packet_ready = false;
    tx_waypoint_pending = false;
    if (txmessage == NULL) {
        return PACKET_FAIL;
    }

    uint8_t mission;
    uint8_t status;
    switch (txmessage->output_data.mission_state) {
    case MISSION_STATE_INITIALIZE: mission = 0U; break;
    case MISSION_STATE_NAVIGATE: mission = 1U; break;
    case MISSION_STATE_COMPLETE: mission = 2U; break;
    default: return PACKET_FAIL;
    }
    switch (txmessage->output_data.data_status) {
    case DATA_UNKNOWN: status = 0U; break;
    case DATA_VALID: status = 1U; break;
    case DATA_INVALID: status = 2U; break;
    default: return PACKET_FAIL;
    }
    if (txmessage->output_data.current_waypoint_index >= WAYPOINT_COUNT_ON_WIRE) {
        return PACKET_FAIL;
    }
    if (txmessage->waypoint_list_valid &&
        txmessage->waypoint_list.waypoint_count != WAYPOINT_COUNT_ON_WIRE) {
        return PACKET_FAIL;
    }

    if (txmessage->waypoint_list_valid) {
        BeginTxPacket(tx_waypoint_packet, 0x03U, WAYPOINT_PAYLOAD_SIZE);
        tx_waypoint_packet[5] = WAYPOINT_COUNT_ON_WIRE;
        for (uint8_t i = 0U; i < WAYPOINT_COUNT_ON_WIRE; ++i) {
            uint16_t offset = (uint16_t)(6U + 12U * i);
            const Waypoint_t *wp = &txmessage->waypoint_list.waypoint[i];
            WriteFloatLE(&tx_waypoint_packet[offset], wp->latitude);
            WriteFloatLE(&tx_waypoint_packet[offset + 4U], wp->longitude);
            WriteFloatLE(&tx_waypoint_packet[offset + 8U], wp->altitude);
        }
        FinishTxPacket(tx_waypoint_packet, WAYPOINT_PAYLOAD_SIZE);
        tx_waypoint_pending = true;
    }

    BeginTxPacket(tx_output_packet, 0x04U, OUTPUT_PAYLOAD_SIZE);
    WriteFloatLE(&tx_output_packet[5], txmessage->output_data.target_altitude);
    WriteFloatLE(&tx_output_packet[9], txmessage->output_data.target_heading);
    WriteFloatLE(&tx_output_packet[13], txmessage->output_data.target_speed);
    tx_output_packet[17] = txmessage->output_data.current_waypoint_index;
    tx_output_packet[18] = mission;
    tx_output_packet[19] = status;
    FinishTxPacket(tx_output_packet, OUTPUT_PAYLOAD_SIZE);
    tx_packet_ready = true;
    return PACKET_SUCCESS;
}

TransmitResult_t Transmit(void)
{
    if (!tx_packet_ready) {
        return TRANSMIT_FAIL;
    }
    /* TX Task only. RX interrupts remain enabled during blocking TX. */
    if (tx_waypoint_pending) {
        if (HAL_UART_Transmit(&huart2, tx_waypoint_packet,
                              sizeof(tx_waypoint_packet), TX_TIMEOUT_MS) != HAL_OK) {
            return TRANSMIT_FAIL;
        }
        tx_waypoint_pending = false;
    }
    if (HAL_UART_Transmit(&huart2, tx_output_packet,
                          sizeof(tx_output_packet), TX_TIMEOUT_MS) != HAL_OK) {
        return TRANSMIT_FAIL;
    }
    tx_packet_ready = false;
    return TRANSMIT_SUCCESS;
}
