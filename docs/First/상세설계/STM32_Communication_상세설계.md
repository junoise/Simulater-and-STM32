## Communication 상세설계

### 1. 목적
- Communication 모듈의 상세 역할을 기술한다.

### 2. 사용 데이터

#### 입력 데이터
- RX Buffer : 
    - Unity 로부터 UART를 통해 수신한 원시 데이터
    - 수신 패킷이 저장되는 BYTE Buffer       

- Output Data : (S2U)
    - TxMessage_t
        - OutputData_t
        - waypoint_list
        - waypoint_list_valid

#### 출력 데이터
- Parsed Data :
    - RxMessage_t
        - AircraftState_t
        - Destination_t
        - destination_valid
        - mission_start_command
        - mission_command_valid
- TX Packet : (S2U)
    - Output Data를 UART 송신 형식으로 변환한 패킷

#### 내부 상태
- Communication Status :
    - 통신 정상 / 통신오류
- RX Status :
    - 최근 데이터 수신 성공 / 최근 데이터 수신 실패
- TX Status :
    - 최근 데이터 송신 성공 / 최근 데이터 송신 실패


### 3. 함수 설계

#### 3.1 [함수명] Receive
- 목적 : Unity로부터 UART를 통해 전송되는 데이터를 수신하여 RX Buffer에 저장한다.
- 호출 Task : Comm Rx Task
- 입력 : UART수신 데이터 
- 출력 :  RX Buffer
- 반환값 : 
    - RX_SUCCESS
    - RX_FAIL
- 처리 절차 :
    1. UART를 통해 수신되는 데이터를 확인한다.
    2. 수신된 데이터를 RX Buffer에 저장한다.
    3. 하나의 패킷 수신이 완료되면 Parse 함수가 처리할 수 있도록 한다.
- 오류 처리 :
    - UART 수신 실패 또는 패킷 수신이 정상적으로 완료되지 않은 경우 RX_FAIL을 반환한다.
    - RX Status를 수신 실패 상태로 갱신한다.

#### 3.2 [함수명] Parse
- 목적 : Buffer에 존재하는 수신 데이터를 STM32형식에 맞게 파싱
- 호출 Task : Comm Rx Task
- 입력 : Unity로부터 UART를 통해 수신한 원시 데이터가 저장된 RX Buffer
- 출력 : 
    - RxMessage_t
        - AircraftState_t
        - Destination_t
        - destination_valid
        - mission_start_command
        - mission_command_valid
- 반환값 :
    - PARSE_SUCCESS
    - PARSE_FAIL

- 처리 절차 :
    0. 새 패킷을 Parse전 RxMessage_t를 초기화한다.
    1. RX Buffer에서 패킷 구분자 및 각 데이터 필드를 확인한다.
    2. 각 필드를 정의된 데이터 타입으로 변환한다.
    3. 주기 상태 데이터는 AircraftState_t에 저장한다.
    4. 목적지 데이터가 포함된 경우 Destination_t에 저장한다.
    5. 임무 시작 명령이 포함된 경우 mission_start_command에 저장한다.
- 오류 처리 :
    - 필드 개수가 정의된 형식과 다르거나 데이터 타입 변환에 실패한 경우 PARSE_FAIL을 반환한다.
    - 파싱 실패한 데이터는 Data Manager로 전달하지 않는다.
    - PARSE_FAIL 상태를 기록하고 해당 데이터를 폐기한다.

#### 3.3 [함수명] CreateTxPacket
- 목적 : Unity로 송신할 데이터를 UART의 형식에 맞게 패킷을 생성한다.
- 호출 Task : Comm Tx Task
- 입력 :
    - TxMessage_t
        - OutputData_t
        - waypoint_list
        - waypoint_list_valid
- 출력 : Tx Buffer(UART를 통해 송신할 수 있도록 변환된 Byte/String 형태의 패킷)
- 반환값 :
    - PACKET_SUCCESS
    - PACKET_FAIL
- 처리 절차 :
    1. TxMessage_t의 데이터 종류와 유효 플래그를 확인한다.
    2. 각 데이터를 정의된 통신 패킷 형식에 맞게 변환한다.
    3. 변환된 패킷을 TX Buffer에 저장한다.
    4. 패킷 생성이 정상적으로 완료되면 PACKET_SUCCESS를 반환한다.
- 오류 처리 :
    - 데이터 변환 또는 패킷 생성에 실패하면 PACKET_FAIL을 반환한다.
    - 생성에 실패한 패킷은 UART로 송신하지 않는다.

#### 3.4 [함수명] Transmit
- 목적 : TX Buffer에 존재하는 송신데이터를 UART를 통해 Unity로 송신한다.
- 호출 Task :  Comm Tx Task
- 입력 : Tx Buffer
- 출력 : -
- 반환값 : 
    - TRANSMIT_SUCCESS
    - TRANSMIT_FAIL
- 처리 절차 :
    1. TX Buffer에 송신할 데이터가 존재하는지 확인한다.
    2. UART를 통해 TX Buffer의 데이터를 Unity로 송신한다.
    3. 송신 완료 여부를 확인하고 TX Status를 갱신한다.
- 오류 처리 :
    - UART 송신에 실패한 경우 TRANSMIT_FATL을 반환한다.
    - TX Status를 송신 실패 상태로 갱신한다.
    - 실패한 패킷의 재송신 여부는 Comm TX Task의 정책에 따라 처리한다.
---

### 4. RX 처리 흐름
1. Comm Rx task가 UART 수신을 대기한다.
2. Recieve 함수가 Unity로 부터 수신된 원시데이터를 RX Buffer에 저장한다.
3. RX_SUCESS라면 Parse함수를 호출한다.
4. Parse함수가 RX Buffer값을 STM32 내부데이터 형식으로 변환한다.
5. PARSE_SUCCESS인 경우 RxMessage_t 전송한다.
6. RX_FAIL 혹은 PARSE_FAIL인 경우 데이터를 전달하지 않고 오류 상태를 갱신한다.

### 5. TX 처리 흐름
1. Comm TX Task가 송신 주기에 따라 실행된다.
2. Comm TX Task가 TX Queue에서 최신 TxMessage_t를 획득한다.
3. CreateTxPacket이 TX Buffer 형식에 맞게 패킷을 생성한다.
4. PACKET_SUCCESS라면 Transmit함수를 호출한다.
5. Transmit함수가 TX Buffer의 데이터를 UART를 통해 unity로 송신한다.
6. 송신 상태에 따라 Tx Status상태를 갱신한다.
7. 송신 실패 시 실패 상태를 기록하고 다음 TX 주기에 최신 데이터를 다시 송신한다.

### 6. FreeRTOS 연계
- RX Task :
    - Unity로부터 UART 데이터를 수신한다.
    - 수신 성공 시 Parse 함수를 호출한다.
    - 파싱된 RxMessage_t를 Data Manager에 전달한다.
- TX Task :
    - OutputData Manger로 부터 데이터를 TX Queue에 전달 받는다.
    - 수신한 데이터를 기반으로 형식에 맞게 패킷을 생성한다.
    - 패킷 생성 성공 시 Transmit함수를 호출한다.
    - UART를 통해 unity로 송신한다.

- 사용 Queue : 
    - RX Queue: 길이 1(Data Manager에서 유효성 검사를 완료한 MissionInput_t를 Mission Task로 전달하는 Queue)
    - TX Queue: 길이 1
- Queue 전달 데이터 : 
    - RX Queue: MissionInput_t
    - TX Queue: TxMessage_t
- Queue Full 시 처리 : 기존 데이터를 제거하고 최신 데이터로 덮어쓴다, 항상 가장 최근 데이터를 유지한다.
- Task 간 공유 자원 : 
    - UART
    - RX Buffer
    - TX Buffer
    

### 7. 오류 처리
| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| UART 수신 실패 | Recieve 함수가 정상적으로 패킷을 수신하지 못함 | 해당 수신 데이터를 폐기하고 다음 수신 주기를 대기한다. Parse함수를 호출하지 않는다. | RX_FAIL |
| 수신 패킷 파싱 실패 | 필드 개수 불일치 또는 데이터 타입 변환 실패 | 해당 패킷을 폐기하고 Data Manager로 전달하지 않는다. | PARSE_FAIL |
| TX 패킷 생성 실패  | 송신 데이터를 정의된 형식으로 변환하지 못함 | 해당 주기의 송신을 수행하지 않고 다음 TX주기에 최신데이터로 다시 패킷 생성을 시도 | PACKET_FAIL |
| UART 송신 실패 | Transmit함수에서 UART 송신이 정상적으로 완료되지 않음. | 실패 상태를 기록하고 다음 TX주기에 최신 데이터를 다시 송신 | TRANSMIT_FAIL |
| 통신 이상 | TBD ms 동안 정상적인 UART 수신이 발생하지 않음 | Communication Status를 오류 상태로 갱신하고 System Monitor에 상태를 제공한다.| COMM_ERROR |
| 통신 정상 복구 | 오류 이후 정상 패킷 수신 확인 | Communication Status를 정상 상태로 복구하고 이후 데이터 처리를 재개한다.| COMM_OK |

