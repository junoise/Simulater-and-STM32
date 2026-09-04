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

Communication Status + current_fuel
          ↓
      Monitor Task
          ↓
    SystemStatus_t
          ↓
System Status Queue
          ↓
      Mission Task


