## Mission Manager 상세설계

### 1. 목적

- 현재 Mission State를 관리한다.
- MissionInput_t와 SystemStatus_t를 기반으로 상태별 처리 함수를 호출하여 임무 흐름을 제어한다.
- 현재 임무 상태와 마지막으로 정상 계산한 목표값을 조회 함수로 Mission Task에 제공한다.

본 문서는 대화에서 확정한 구현 방향을 반영한다. OutputData Manager 및 Task 연결이 아직 완료되지 않은 부분은 별도로 표시한다.

### 2. 사용 데이터

#### 입력 데이터

- MissionInput_t
  - aircraft_state
  - destination
  - destination_valid
  - mission_start_command
  - mission_command_valid
- SystemStatus_t
  - 현재 구현에서는 communication_status를 확인한다.

#### 입력 규칙

- 초기 목적지와 임무 시작 명령은 Unity에서 같은 메시지로 전달한다.
- destination_valid는 이번 메시지에 유효한 새 목적지가 포함되었음을 나타낸다.
- 목적지는 초기 시작 또는 변경 시에만 전달한다.
- destination_valid가 false이면 해당 메시지의 destination 필드는 사용하지 않는다. 기존 Waypoint 목록을 지우라는 의미가 아니다.
- mission_command_valid는 이번 메시지에 유효한 임무 명령이 포함되었음을 나타낸다.
- Data Manager에서 유효성 검사를 수행한 MissionInput_t를 입력으로 사용한다. 시작 명령의 유효 범위는 0 또는 1이며, 시작 값은 1이다.
- 초기 목적지와 시작 요청을 서로 다른 메시지에서 모으는 별도 저장 기능은 현재 범위에 포함하지 않는다.

#### 출력 데이터

- GetMissionState()의 반환값: 현재 MissionState_t
- GetTargetCommand()의 출력 인자: 마지막 정상 TargetCommand_t의 복사본
- WaypointList_t와 current_waypoint_index는 Waypoint Manager가 소유하며 기존 GetWaypointList(), GetCurrentWaypointIndex()로 조회한다.
- Mission Manager는 현재 TxMessage_t를 직접 생성하거나 Queue에 전달하지 않는다.

#### 내부 상태

- static MissionState_t missionstate
  - 초기값: MISSION_STATE_INITIALIZE
  - 상태: MISSION_STATE_INITIALIZE, MISSION_STATE_NAVIGATE, MISSION_STATE_COMPLETE
- static TargetCommand_t cur_target_command
  - 초기값: 모든 필드 0
  - 마지막으로 정상 계산한 목표 heading, altitude, speed 저장
- static bool target_command_valid
  - 초기값: false
  - Guidance 계산 성공 이후 true
  - 초기 0값이 정상 목표값으로 조회되는 것을 방지한다.
  - 현재 구현에서는 이후 계산 실패 또는 임무 완료 시 false로 초기화하지 않는다.
  - true는 정상 목표값을 생성한 이력이 있다는 뜻이며, 이번 주기 계산 성공이나 변경된 목적지에 대한 목표값 생성을 보장하지 않는다.

---

### 3. 함수 설계

#### 공개 범위

- mission_manager.h에 공개: ProcessMission(), GetMissionState(), GetTargetCommand()
- mission_manager.c 내부 static 함수: ProcessInitialize(), ProcessNavigate(), ProcessMissionComplete()
- 상태별 처리 함수는 ProcessMission()만 호출한다.
- 입력 포인터의 NULL 검사와 통신 상태 검사는 ProcessMission()에서 수행한다. 내부 함수는 이 검사를 통과한 입력을 사용한다.

#### 3.1 ProcessMission

- 목적: 입력과 현재 Mission State를 확인하고 상태에 맞는 임무 처리를 수행한다.
- 호출자: Mission Task
- 입력:
  - const MissionInput_t *mission_input
  - const SystemStatus_t *SystemStatus
- 출력: 없음. 처리 결과는 내부 상태에 반영하며 조회 함수로 제공한다.
- 반환값:
  - MISSION_PROCESS_SUCCESS
  - MISSION_PROCESS_FAIL
- 처리 절차:
  1. mission_input 또는 SystemStatus가 NULL이면 MISSION_PROCESS_FAIL을 반환한다.
  2. communication_status가 COMM_ERROR이면 현재 Mission State를 유지하고 MISSION_PROCESS_FAIL을 반환한다.
  3. MISSION_STATE_INITIALIZE이면 ProcessInitialize()를 호출한다.
     - INITIALIZE_FAIL이면 MISSION_PROCESS_FAIL을 반환한다.
     - INITIALIZE_WAIT는 오류로 처리하지 않는다. 상태를 유지하고 MISSION_PROCESS_SUCCESS를 반환한다.
     - INITIALIZE_SUCCESS이면 NAVIGATE로 전이한 상태에서 MISSION_PROCESS_SUCCESS를 반환한다. 같은 호출에서 ProcessNavigate()까지 실행하지 않는다.
  4. MISSION_STATE_NAVIGATE이면 다음 순서로 처리한다.
     - destination_valid가 true이면 현재 AircraftState와 새 Destination으로 GenerateWaypointList()를 호출한다.
     - 생성에 실패하면 해당 주기의 항법 처리를 수행하지 않고 MISSION_PROCESS_FAIL을 반환한다.
     - destination_valid가 false이면 기존 목록을 사용한다.
     - ProcessNavigate()를 호출한다. NAVIGATE_FAIL이면 MISSION_PROCESS_FAIL을 반환한다.
     - NAVIGATE_SUCCESS 또는 NAVIGATE_COMPLETE이면 MISSION_PROCESS_SUCCESS를 반환한다.
  5. MISSION_STATE_COMPLETE이면 ProcessMissionComplete()를 호출하고 MISSION_PROCESS_SUCCESS를 반환한다.
  6. 정의되지 않은 상태이면 MISSION_PROCESS_FAIL을 반환한다.
- 주의 사항:
  - MISSION_PROCESS_SUCCESS는 새 목표값이 생성되었다는 의미가 아니다.
  - 초기화 대기, 초기화 성공, 최종 도달, 완료 상태 유지에서도 성공을 반환할 수 있다.
  - 현재 COMPLETE 상태에서 새 목적지나 시작 명령으로 재시작하는 동작은 구현하지 않았다.

#### 3.2 ProcessInitialize

- 공개 범위: static 내부 함수
- 목적: 초기 목적지와 시작 명령을 확인하여 Waypoint 목록을 생성하고 NAVIGATE로 전이한다.
- 호출자: ProcessMission()
- 호출 조건: missionstate == MISSION_STATE_INITIALIZE
- 입력: const MissionInput_t *mission_input
  - 현재 AircraftState도 Waypoint 생성에 사용한다.
- 출력: 없음
- 반환값: INITIALIZE_SUCCESS, INITIALIZE_WAIT, INITIALIZE_FAIL
- 처리 절차:
  1. destination_valid가 false이면 INITIALIZE_WAIT를 반환한다.
  2. mission_start_command가 0이면 INITIALIZE_WAIT를 반환한다.
  3. mission_command_valid가 false이면 INITIALIZE_WAIT를 반환한다.
  4. 현재 AircraftState와 Destination을 GenerateWaypointList()에 전달한다.
  5. 생성에 실패하면 INITIALIZE 상태를 유지하고 INITIALIZE_FAIL을 반환한다.
  6. 생성에 성공하면 missionstate를 MISSION_STATE_NAVIGATE로 변경하고 INITIALIZE_SUCCESS를 반환한다.
- 주의 사항:
  - 명령값은 Data Manager에서 0 또는 1로 검증되어 있다는 전제를 사용한다.
  - 이 함수는 Guidance를 계산하지 않으며 target_command_valid를 true로 만들지 않는다.
  - 초기 목록 생성 사실을 송신 경로에 전달하는 연결은 4절의 미완료 항목을 따른다.

#### 3.3 ProcessNavigate

- 공개 범위: static 내부 함수
- 목적: Waypoint 진행 상태를 판단하고, 임무 진행 중이면 Guidance를 계산하여 내부 목표값을 갱신한다.
- 호출자: ProcessMission()
- 호출 조건: missionstate == MISSION_STATE_NAVIGATE, 상위 입력·통신 검사 통과
- 입력:
  - const AircraftState_t *aircraftstate
  - const SystemStatus_t *SystemStatus
    - 마지막 제시 코드의 인자를 유지한 것이다. 통신 검사를 상위로 옮겼으므로 현재 함수 본문에서는 사용하지 않는다.
- 출력: 외부 출력 인자 없음. 내부 cur_target_command와 target_command_valid 갱신
- 반환값: NAVIGATE_SUCCESS, NAVIGATE_COMPLETE, NAVIGATE_FAIL
- 처리 절차:
  1. UpdateWaypointProgress(aircraftstate)를 호출한다.
  2. WAYPOINT_CHECK_FAIL이면 NAVIGATE_FAIL을 반환한다.
  3. FINAL_WAYPOINT_REACHED이면 missionstate를 MISSION_STATE_COMPLETE로 변경하고 NAVIGATE_COMPLETE를 반환한다. 추가 Guidance 계산은 수행하지 않는다.
  4. WAYPOINT_IN_PROGRESS이면 GetCurrentWaypoint()로 현재 목표 Waypoint를 지역변수에 복사한다.
  5. 조회에 실패하면 NAVIGATE_FAIL을 반환한다.
  6. 현재 AircraftState, 조회한 Waypoint, cur_target_command의 주소를 CalculateGuidance()에 전달한다.
  7. Guidance 계산에 실패하면 NAVIGATE_FAIL을 반환한다.
  8. 계산 성공 시 target_command_valid를 true로 설정하고 NAVIGATE_SUCCESS를 반환한다.
  9. 정의되지 않은 진행 결과이면 NAVIGATE_FAIL을 반환한다.
- 오류 처리 및 상태 유지:
  - 현재 Guidance 구현은 실패 반환 전에 목표값을 변경하지 않으므로 마지막 정상 cur_target_command가 유지된다.
  - 목표값을 생성한 적이 없다면 target_command_valid는 false로 유지된다.
  - Waypoint 진행 판단이 먼저 실행되므로 이후 Guidance 실패가 앞서 변경된 Waypoint 인덱스를 되돌리지는 않는다.
  - 목표 heading 계산, Waypoint 고도 적용, 임시 200m/s 목표속도 설정은 Guidance 모듈의 책임이다.

#### 3.4 ProcessMissionComplete

- 공개 범위: static 내부 함수
- 목적: 완료 상태에서 추가 임무 계산 없이 정상 종료한다.
- 호출자: ProcessMission()
- 호출 조건: missionstate == MISSION_STATE_COMPLETE
- 입력: 없음
- 출력: 없음
- 반환값: MISSION_COMPLETE_SUCCESS
- 처리 절차:
  1. Waypoint 진행 및 Guidance 계산을 수행하지 않는다.
  2. 내부 상태와 마지막 목표값을 변경하지 않는다.
  3. MISSION_COMPLETE_SUCCESS를 반환한다.
- 현재 상태 조회는 GetMissionState()를 통해 수행한다.

#### 3.5 GetMissionState

- 공개 범위: mission_manager.h
- 목적: 현재 임무 상태를 외부에 제공한다.
- 호출자: Mission Task
- 입력: 없음
- 출력 인자: 없음
- 반환값: 현재 missionstate의 값(MissionState_t)
- 처리 절차: 내부 missionstate를 반환한다. 내부 상태를 수정하지 않는다.

#### 3.6 GetTargetCommand

- 공개 범위: mission_manager.h
- 목적: 마지막으로 정상 계산한 목표값을 외부 변수에 복사한다.
- 호출자: Mission Task
- 입력 데이터: 없음
- 출력 인자: TargetCommand_t *target_command
- 반환값:
  - true: 정상 목표값을 복사함
  - false: 출력 포인터가 NULL이거나 아직 정상 목표값을 생성하지 못함
- 처리 절차:
  1. target_command가 NULL이면 false를 반환한다.
  2. target_command_valid가 false이면 false를 반환한다.
  3. cur_target_command를 출력 인자가 가리키는 변수에 복사한다.
  4. true를 반환한다.
- 주의 사항:
  - false를 반환하면 호출자의 출력 변수를 수정하지 않는다.
  - 계산 실패 이후와 임무 완료 상태에서도 이전 정상 목표값은 조회 가능하다.
  - 호출자는 ProcessMission() 결과 및 Mission State와 함께 해석해야 한다.
  - 조회 성공만으로 현재 목적지에 유효한 새 명령이라고 판단해서는 안 된다.

---

### 4. 처리 흐름

1. Mission Task가 RX Queue에서 MissionInput_t를 받고 System Status Queue에서 사용할 SystemStatus_t를 확보한다.
2. ProcessMission()으로 입력·통신 상태 검사 및 임무 상태별 처리를 수행한다.
3. INITIALIZE에서는 같은 메시지의 목적지와 시작 명령을 확인하고 초기 Waypoint 목록을 생성한다. 성공하면 NAVIGATE로 전이한다.
4. NAVIGATE에서는 새 목적지가 포함된 경우 먼저 목록을 재생성한다. 이후 도달 여부를 판단하고, 진행 중이면 Guidance를 계산하거나 최종 도달이면 COMPLETE로 전이한다.
5. COMPLETE에서는 추가 항법 계산을 수행하지 않는다.
6. Mission Task는 GetMissionState()와 GetTargetCommand()로 상태와 목표값을 조회한다.
7. Waypoint 목록과 현재 인덱스는 Waypoint Manager의 기존 조회 함수로 획득한다.
8. Mission Task에서 OutputData Manager를 사용해 송신 메시지를 구성하고 TX Queue로 전달하는 흐름으로 연결한다.
9. Comm TX Task는 TX Queue의 메시지를 이용해 패킷 생성과 UART 송신을 수행한다.

#### 출력 연결 시 남은 사항

- 6~9번의 최종 Task·OutputData 연결은 아직 구현 완료로 간주하지 않는다.
- 초기화 대기 및 최초 Guidance 성공 전에는 GetTargetCommand()가 false이다. 이 경우 목표값을 포함한 메시지를 보낼지 여부와 형식은 출력 정책에서 정해야 한다.
- 처리 실패와 완료 상태에서 이전 목표값을 어떻게 송신·사용할지 출력 정책에서 구분해야 한다.
- 새 Waypoint 목록은 초기 생성 또는 변경 시에만 송신하고, current_waypoint_index는 주기 송신한다는 기존 정책을 유지한다.
- 두 조회 함수만으로는 이번 주기에 목록이 새로 생성됐는지 알 수 없다. 초기 생성·재생성 성공 사실을 OutputData Manager에 전달하고 waypoint_list_valid에 반영하는 방법은 아직 연결하지 않았다.
- 목적지 재생성 실패 시 기존 목록이 보존되는지는 Waypoint Manager 구현에 달려 있다. Mission Manager는 이전 목록 복원을 수행하지 않는다.

### 5. FreeRTOS 연계

- 실행 Task: Mission Task
- Mission Manager 함수 자체의 직접 Queue 사용: 없음
- 연계 Queue와 전달 자료형:

| Queue | 역할 | 전달 자료형 | 설계 길이 |
|---|---|---|---:|
| RX Queue | Comm RX Task에서 Mission Task로 입력 전달 | MissionInput_t | 1 |
| System Status Queue | Monitor Task에서 Mission Task로 상태 전달 | SystemStatus_t | 1 |
| TX Queue | Mission Task 측 출력 구성 결과를 Comm TX Task로 전달 | TxMessage_t | 1 |

- 최신 데이터 우선 정책은 기존 설계 방향이다. Queue Full 처리와 송신 성공 확인은 Task·Queue 구현에서 수행한다.
- 일반 osMessageQueuePut() 호출만으로 길이 1 Queue의 기존 값이 자동 덮어써지는 것은 아니므로 실제 처리와 정책을 대조해야 한다.
- 목적지·시작 명령·새 Waypoint 목록은 일회성 정보다. 최신값 덮어쓰기로 소비 전에 유실되지 않도록 보존·전달 정책을 Task 연결 시 확인해야 한다.
- 내부 static 상태는 Mission Task에서만 접근하는 것을 전제로 한다. 현재 별도 Task가 조회 함수를 동시에 호출하는 구조는 사용하지 않는다.

### 6. 오류 및 경계 처리

| 상황 | 판단 기준 | 처리 방법 | 결과 |
|---|---|---|---|
| 입력 포인터 없음 | mission_input 또는 SystemStatus가 NULL | 상태별 처리 중단, 내부 상태 유지 | MISSION_PROCESS_FAIL |
| 통신 오류 | communication_status == COMM_ERROR | 해당 주기 처리 중단, Mission State 및 목표값 유지 | MISSION_PROCESS_FAIL |
| RX 입력 미수신 | Mission Task가 입력을 획득하지 못함 | Task에서 ProcessMission 호출 안 함 | 모듈 반환값 없음 |
| 초기화 대기 | 목적지 또는 유효한 시작 명령 미포함, 시작 명령 0 | INITIALIZE 유지 | 내부 INITIALIZE_WAIT, 외부 MISSION_PROCESS_SUCCESS |
| 초기 목록 생성 실패 | GenerateWaypointList 실패 | INITIALIZE 유지 | INITIALIZE_FAIL → MISSION_PROCESS_FAIL |
| 비행 중 목록 재생성 실패 | 새 목적지 처리 시 GenerateWaypointList 실패 | NAVIGATE 유지, 해당 주기 ProcessNavigate 호출 안 함 | MISSION_PROCESS_FAIL |
| Waypoint 진행 판단 실패 | WAYPOINT_CHECK_FAIL 또는 정의되지 않은 결과 | 해당 주기 계산 중단 | NAVIGATE_FAIL → MISSION_PROCESS_FAIL |
| 현재 Waypoint 조회 실패 | GetCurrentWaypoint가 false | Guidance 호출 안 함 | NAVIGATE_FAIL → MISSION_PROCESS_FAIL |
| Guidance 계산 실패 | GUIDANCE_CALCULATE_FAIL | 마지막 정상 목표값 유지, NAVIGATE 유지 | NAVIGATE_FAIL → MISSION_PROCESS_FAIL |
| 최종 도달 | FINAL_WAYPOINT_REACHED | COMPLETE 전이, Guidance 호출 안 함 | 내부 NAVIGATE_COMPLETE, 외부 MISSION_PROCESS_SUCCESS |
| 정의되지 않은 Mission State | 유효한 3개 상태 이외 | 상태별 처리 안 함 | MISSION_PROCESS_FAIL |
| 목표값 조회 불가 | 출력 포인터 NULL 또는 target_command_valid == false | 출력 변수 변경 안 함 | false |

### 7. 검증 항목

아래는 확인할 항목이며 수행 완료 결과가 아니다.

- 입력 NULL 및 COMM_ERROR에서 실패를 반환하고 내부 상태를 유지하는지 확인한다.
- 초기 목적지·시작 명령이 함께 들어오면 Waypoint 생성 후 NAVIGATE로 전이하는지 확인한다.
- 초기화 대기와 초기화 직후 최초 Guidance 실행 전에는 목표값 조회가 실패하는지 확인한다.
- 정상 NAVIGATE에서 Guidance 결과 저장과 조회가 일치하는지 확인한다.
- 목적지가 없는 주기에는 재생성하지 않고 기존 목록의 진행 상태를 갱신하는지 확인한다.
- 새 목적지가 있는 주기에는 재생성 후 새 목록으로 항법을 수행하는지 확인한다.
- 생성·진행·조회·Guidance 실패가 상위 실패 반환으로 전달되는지 확인한다.
- 최초 계산 실패 시 목표값 조회는 false, 정상 계산 이후 실패 시 마지막 정상 목표값 조회는 가능한지 확인한다.
- 최종 도달 후 COMPLETE를 유지하고 추가 Guidance를 수행하지 않는지 확인한다.
- Task 출력 연결 시 생성 이벤트 유실 여부 및 이전 목표값과 새 목표값의 구분을 확인한다.
