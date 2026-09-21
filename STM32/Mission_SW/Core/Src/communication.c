#include "communication.h"
#include <stddef.h>

#define RX_BUFFER_SIZE 256U
static uint8_t rx_buffer[RX_BUFFER_SIZE];
static volatile uint16_t rx_write_index = 0U;
static volatile uint16_t rx_read_index = 0U;  //원형 버퍼 설정.

static uint8_t header_buffer[3] = { 0U };
static uint8_t payload_buffer[24] = { 0U };
static uint8_t crc_buffer[2] = { 0U };
static uint16_t receive_num = 0U;
static uint16_t payload_long = 0U;
static uint32_t receive_time = 0U; //패킷조립 내부변수

static bool rx_packet_ready = false;
static bool rx_packet_error = false; //crc

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
static bool PopRxByte(uint8_t *data) //버퍼함수
{
	if (data == NULL) {
		return false;
	}

	if (rx_read_index == rx_write_index) {
		return false; //버퍼가 비어있음
	}

	data = rx_buffer[rx_read_index];
	rx_read_index = (uint16_t) ((rx_write_index + 1U) % RX_BUFFER_SIZE);

	return true;
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
    /* 완성된 패킷 또는 이미 발생한 오류는 그대로 유지 */
    if (rx_packet_ready || rx_packet_error) {
        return;
    }

    /* 아직 시작 표시를 받지 않았다면 제한시간 없음 */
    if (rx_status == WAIT_AA) {
        return;
    }

    uint32_t elapsed = (uint32_t)(HAL_GetTick() - receive_time);

    if (elapsed >= RX_PACKET_TIMEOUT_MS) {
        ResetRxPacket();
        rx_packet_error = true;
    }
}
