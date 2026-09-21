

#ifndef COMMUNICATION_H_
#define COMMUNICATION_H_

#include "app_types.h"

RxResult_t Receive(void);
ParseResult_t Parse(RxMessage_t *rxmessage);

PacketResult_t CreateTxPacket(const TxMessage_t *txmessage);
RxResult_t StartReceive(void);
TransmitResult_t Transmit(void);
CommunicationStatus_t GetCommunicationStatus(void);
#endif
