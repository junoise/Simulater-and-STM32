

#ifndef COMMUNICATION_H_
#define COMMUNICATION_H_

#include "app_types.h"

RxResult_t StartReceive(void);
RxResult_t Receive(void);
ParseResult_t Parse(RxMessage_t *rxmessage);



PacketResult_t CreateTxPacket(const TxMessage_t *txmessage);
TransmitResult_t Transmit(void);
/* TX Task only. Zero means no 0x03 packet has completed successfully. */
uint64_t GetTransmittedWaypointVersion(void);
CommunicationStatus_t GetCommunicationStatus(void);
void ConfirmRxMessageDelivered(void);
#endif
