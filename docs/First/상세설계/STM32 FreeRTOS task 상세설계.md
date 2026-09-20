## FreeRTOS 전체 설계

### 1. Task 구성

| Task | 역할 | 우선순위 | 주기 |
|---|---|---:|---|
| Comm RX Task | UART 수신, Parse, Data Manager 유효성 검사, RX Queue 전달 | High | 수신 이벤트 기반 / TBD |
| Mission Task | Mission Manager, Waypoint Manager, Guidance, OutputData Manager 수행 | High | 100 ms |
| Comm TX Task | TX Queue 수신, 패킷 생성, UART 송신 | Mid | 100 ms |
| Monitor Task | Communication / Fuel 상태 확인, SystemStatus_t 생성 | Low | 200 ms |

### Task Priority
Comm RX Task : Priority 4
Mission Task : Priority 3
Comm TX Task : Priority 2
Monitor Task : Priority 1

### Task 주기 선정 근거

- Comm RX Task
  - Unity의 UART 데이터 수신 시점에 즉시 처리해야 하므로 고정 주기 방식보다 수신 이벤트 기반으로 동작한다.

- Mission Task
  - 최신 Aircraft State를 기반으로 Waypoint 진행 판단과 Guidance 계산을 수행한다.
  - 임무 명령 갱신이 지나치게 느리지 않도록 100 ms 주기로 설정한다.
  - 추후 Unity 상태 데이터 송신 주기와 End-to-End 지연 요구사항을 기준으로 재조정한다.

- Comm TX Task
  - Mission Task에서 생성된 최신 Target Command를 Unity에 전달한다.
  - Mission 결과보다 지나치게 느리지 않도록 Mission Task와 유사한 수준인 100~150 ms 범위로 설정한다.
  - TX Queue는 길이 1의 최신값 덮어쓰기 방식이므로 이전 데이터가 누적되지 않는다.

- Monitor Task
  - Communication/Fuel 상태는 Guidance 계산보다 빠른 갱신이 필요하지 않으므로 Mission Task보다 느린 200 ms 주기로 설정한다.
  - Communication Timeout 판단 기준보다 충분히 짧은 주기로 유지한다.
  
### 2. Queue 구성

| Queue | Producer | Consumer | 데이터 타입 | 길이 | Full 처리 |
|---|---|---|---|---:|---|
| RX Queue | Comm RX Task | Mission Task | MissionInput_t | 1 | 최신 데이터로 덮어쓰기 |
| System Status Queue | Monitor Task | Mission Task | SystemStatus_t | 1 | 최신 상태로 덮어쓰기 |
| TX Queue | Mission Task | Comm TX Task | TxMessage_t | 1 | 최신 데이터로 덮어쓰기 |


### 전체 흐름
Unity
  ↓
Comm RX Task
  - Receive
  - Parse
  - Data Manager
  ↓
RX Queue 
  ↓    <-----System Status Queue
Mission Task        
  - Mission Manager
  - Waypoint Manager
  - Guidance
  - OutputData Manager
  ↓
TX Queue 
  ↓
Comm TX Task
  - CreateTxPacket
  - Transmit
  ↓
Unity

### SystemMonitor

GetCommunicationStatus() + GetCurrentFuel() 조회 성공값
          ↓
      Monitor Task
          ↓
    SystemStatus_t
          ↓
System Status Queue
          ↓
      Mission Task



### 3. Monitor 상태 공급과 최초 수신

- 기존 4 Task와 3 Queue 구성을 유지한다.
- Communication은 마지막 정상 패킷 수신 이력·시각을 저장한다. Monitor Task는 GetCommunicationStatus()로 미수신/타임아웃을 확인한다.
- Monitor Task는 GetCurrentFuel() 성공 시에만 CheckFuelStatus()로 연료 상태를 갱신한다. 최초 유효 연료 확보 전에는 System Status Queue 전송을 보류한다.
- Mission Task는 최초 SystemStatus_t가 도착하기 전 INITIALIZE에서 임무 처리를 기다린다. 이때 Monitor Task 실행을 막는 busy wait를 사용하지 않는다.
- 대기 중 초기 목적지·시작 명령이 포함된 MissionInput_t를 버리거나 일반 주기 데이터로 덮어써서는 안 된다. 초기 입력 보존과 Queue 소비 순서는 Task 연결 시 검증한다.
- 최초 준비 후에는 연료 조회 실패 여부와 무관하게 저장된 연료 상태와 최신 통신 상태를 전달한다. 이후 새 시스템 상태가 없으면 마지막 수신 상태를 사용한다.
- RX 데이터가 끊겨도 Monitor Task의 타임아웃 판단은 계속되어야 한다. Mission Task의 RX 무기한 대기가 통신 오류 확인을 가로막지 않도록 유한 대기 또는 상태 확인 주기 등의 구체 방식을 구현 시 확정한다. 새 Aircraft State 없이 Guidance를 반복 계산하지 않는다.
- 통신 오류가 생겼다는 사실을 Unity에 보고할 송신 흐름은 OutputData/Comm TX 연결에서 함께 확인한다.
- Queue 최신값 정책은 별도 구현이 필요하다. 일반 osMessageQueuePut()은 Full 시 자동 덮어쓰기를 제공하지 않는다.
- 초기 목적지·시작 명령과 변경 Waypoint 목록 같은 이벤트 데이터는 소비 전 덮어쓰기로 유실되지 않도록 별도 확인한다.
- GetDataStatus()는 데이터 유효성 판단용이며 통신 정상 여부를 대신하지 않는다.
- 통신 타임아웃, 연료 기준값과 공유 데이터 동기화 방식은 TBD다. 표의 Task 주기·우선순위는 설계 초기값이며 코드와 실측 확인이 필요하다.