## Mission Manager 상세설계

### 1. 목적
- 현재 Mission State를 관리하고,
  MissionInput_t와 SystemStatus_t를 기반으로 상태별 필요한 모듈을 호출하여 임무 흐름을 제어한다.

### 2. 사용 데이터

#### 입력 데이터
- MissionInput_t
- SystemStatus_t

#### 출력 데이터
- MissionState_t
- TargetCommand_t
- WaypointList_t
- current_waypoint_index

#### 내부 상태
- MissionState_t
    - INITIALIZE
    - NAVIGATE
    - MISSION_COMPLETE

---

### 3. 함수 설계

#### 3.1 ProcessMission
- 목적 : 현재 Mission State와 입력 상태를 확인하고, Mission State에 따라 필요한 Mission 처리 함수를 호출한다.
- 호출 Task : Mission Task
- 입력 : 
    - MissionInput_t
    - SystemStatus_t
- 출력 : 없음
- 반환값 :
    - MISSION_PROCESS_SUCCESS
    - MISSION_PROCESS_FAIL
- 처리 절차 :
    
    1. 현재 MissionState_t를 확인한다.
    2. SystemStatus_t를 확인하여 임무 처리가 가능한 상태인지 판단한다.
    3. Mission State가 INITIALIZE인 경우 ProcessInitialize()를 호출한다.
    4. Mission State가 NAVIGATE인 경우 ProcessNavigate()를 호출한다.
    5. Mission State가 MISSION_COMPLETE인 경우 ProcessMissionComplete()를 호출한다.
    6. 상태별 처리가 정상적으로 완료되면 MISSION_PROCESS_SUCCESS를 반환한다
- 오류 처리 :
    - 정의되지 않은 Mission State인 경우 MISSION_PROCESS_FAIL을 반환한다.
    - SystemStatus_t가 임무 수행 불가 상태인 경우 해당 주기의 임무 처리를 수행하지 않는다.
    - 호출한 상태 처리 함수가 실패한 경우 MISSION_PROCESS_FAIL을 반환한다.

#### 3.2 ProcessInitialize
- 목적 :INITIALIZE 상태에서 임무 시작에 필요한 초기 데이터를 확인하고 Waypoint 목록을 생성한 뒤 NAVIGATE 상태로 전이한다.
- 호출 Task : Mission Task
- 호출 조건 : MissionState_t == INITIALIZE
- 입력 :
    - Destination_t
    - destination_valid
    - mission_start_command
    - mission_command_valid
- 출력 : 없음
- 반환값 :
    - INITIALIZE_SUCCESS
    - INITIALIZE_WAIT
    - INITIALIZE_FAIL

- 처리 절차 :
    1. MissionInput_t의 destination_valid 값을 확인한다.
    2. mission_command_valid 값을 확인한다.
    3. Destination과 Mission Start Command가 준비되지 않은 경우 INITIALIZE_WAIT를 반환하고 INITIALIZE 상태를 유지한다.
    4. mission_start_command가 임무 시작 값인지 확인한다.
    5. 유효한 Destination이 존재하고 임무 시작 명령이 확인되면 Waypoint Manager의 GenerateWaypointList()를 호출한다.
    6. Waypoint 생성에 성공하면 필요한 초기 출력 데이터를 준비한다.
    7. MissionState_t를 NAVIGATE로 변경한다.
    8. INITIALIZE_SUCCESS를 반환한다.
- 오류 처리 :
    - Destination 데이터가 유효하지 않은 경우 Waypoint를 생성하지 않고 INITIALIZE 상태를 유지한다.
    - Mission Start Command가 유효하지 않은 경우 임무를 시작하지 않고 INITIALIZE 상태를 유지한다.
    - GenerateWaypointList()가 WAYPOINT_GENERATE_FAIL을 반환한 경우 INITIALIZE_FAIL을 반환하고 INITIALIZE 상태를 유지한다.

#### 3.3 ProcessNavigate
- 목적 : NAVIGATE 상태에서 현재 Aircraft 상태를 기반으로 Waypoint 진행 상태를 확인하고,임무가 완료되지 않은 경우 Guidance를 계산한다.
- 호출 Task : Mission Task
- 호출 조건 : MissionState_t == NAVIGATE
- 입력 :
    - AircraftState_t
    - SystemStatus_t
- 출력 : TargetCommand_t
- 반환값 :
    - NAVIGATE_SUCCESS
    - NAVIGATE_COMPLETE
    - NAVIGATE_FAIL
- 처리 절차 :
    1. SystemStatus_t를 확인하여 현재 임무 수행이 가능한 상태인지 확인한다.
    2. MissionInput_t에서 최신 AircraftState_t를 확인한다.
    3. Waypoint Manager의 UpdateWaypointProgress()를 호출하여 현재 Waypoint 진행 상태를 확인한다.
    4. 반환값이 WAYPOINT_CHECK_FAIL인 경우 NAVIGATE_FAIL을 반환한다.
    5. 반환값이 FINAL_WAYPOINT_REACHED인 경우 MissionState_t를 MISSION_COMPLETE로 전이하고 NAVIGATE_COMPLETE를 반환한다.
    6. WAYPOINT_IN_PROGRESS인 경우 Guidance의 CalculateGuidance()를 호출한다.
    7. Guidance 계산에 성공하면 TargetCommand_t를 갱신한다.
    8. NAVIGATE_SUCCESS를 반환한다.
- 오류 처리 :
    - SystemStatus_t가 임무 수행 불가 상태인 경우 Guidance 계산을 수행하지 않고 해당 주기의 NAVIGATE 처리를 중단한다.
    - UpdateWaypointProgress()가 실패한 경우 기존 Waypoint 상태를 유지하고 NAVIGATE_FAIL을 반환한다.
    - CalculateGuidance()가 실패한 경우 기존 TargetCommand_t를 유지하고 NAVIGATE_FAIL을 반환한다.

#### 3.4 ProcessMissionComplete
- 목적 :MISSION_COMPLETE 상태에서 임무 완료 상태를 유지하고, 추가적인 Waypoint 진행 및 Guidance 계산을 수행하지 않는다.
- 호출 Task : Mission Task
- 호출 조건 : MissionState_t == MISSION_COMPLETE
- 입력 : 없음
- 출력 : MissionState_t
- 반환값 : MISSION_COMPLETE_SUCCESS
- 처리 절차 :
    1. 현재 MissionState_t가 MISSION_COMPLETE인지 확인한다.
    2. Waypoint 진행 처리 및 Guidance 계산을 수행하지 않는다.
    3. MissionState_t를 MISSION_COMPLETE 상태로 유지한다.
    4. 임무 완료 상태를 OutputData Manager가 사용할 수 있도록 제공한다.
    5. MISSION_COMPLETE_SUCCESS를 반환한다.
- 오류 처리 : 없음



---

### 4. 처리 흐름
1. Mission Task가 RX Queue에서 최신 MissionInput_t를 수신하고, System Status Queue에서 최신 SystemStatus_t를 확인한다.

2. ProcessMission()을 호출하여 현재 MissionState_t를 확인하고 상태에 맞는 처리 함수를 선택한다.

3. INITIALIZE 상태인 경우 ProcessInitialize()를 호출하여 Destination과 Mission Start Command를 확인하고 Waypoint 목록을 생성한다. 초기화가 정상적으로 완료되면 MissionState_t를 NAVIGATE로 전이한다.

4. NAVIGATE 상태인 경우 ProcessNavigate()를 호출한다.
   - UpdateWaypointProgress()를 통해 Waypoint 진행 상태를 확인한다.
   - 최종 Waypoint에 도달한 경우 MissionState_t를 MISSION_COMPLETE로 전이한다.
   - 임무가 진행 중인 경우 CalculateGuidance()를 호출하여 TargetCommand_t를 생성한다.

5. MISSION_COMPLETE 상태인 경우 ProcessMissionComplete()를 호출하여 추가 Waypoint 처리 및 Guidance 계산을 중단하고 임무 완료 상태를 유지한다.

6. Mission Manager에서 생성 또는 획득한 MissionState_t, TargetCommand_t, Waypoint 관련 데이터를 OutputData Manager에 전달한다.

### 5. FreeRTOS 연계
- 사용 Task : Mission Task
- 사용 Queue :
    - RX Queue : 길이 1
    - System Status Queue : 길이 1
    - TX Queue : 길이 1

- Queue 전달 데이터 :
    - RX Queue : MissionInput_t
    - System Status Queue : SystemStatus_t
    - TX Queue : TxMessage_t

- Queue Full 시 처리 :
    - RX Queue와 System Status Queue는 최신 데이터로 덮어쓴다.
    - TX Queue도 최신 송신 데이터로 덮어써 항상 가장 최근 값을 유지한다.
- Task 간 공유 자원 : 없음

### 6. 오류 처리

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Communication Error | `SystemStatus_t.communication_status == COMM_ERROR` | 해당 주기의 Mission 처리를 중단하고 Guidance 계산을 수행하지 않는다. 현재 Mission State는 유지한다. | `COMM_ERROR`, `MISSION_PROCESS_FAIL` |
| Mission Input 없음 | RX Queue에서 유효한 `MissionInput_t`를 획득하지 못함 | 해당 주기의 Mission 처리를 수행하지 않고 현재 Mission State를 유지한다. | `MISSION_PROCESS_FAIL` |
| INITIALIZE 대기 | Destination 또는 Mission Start Command가 아직 준비되지 않음 | 오류로 처리하지 않고 `INITIALIZE` 상태를 유지한 채 다음 주기를 대기한다. | `INITIALIZE_WAIT` |
| Waypoint 생성 실패 | `GenerateWaypointList()`가 `WAYPOINT_GENERATE_FAIL` 반환 | `NAVIGATE`로 전이하지 않고 `INITIALIZE` 상태를 유지한다. | `INITIALIZE_FAIL` |
| Waypoint 진행 처리 실패 | `UpdateWaypointProgress()`가 `WAYPOINT_CHECK_FAIL` 반환 | current waypoint 상태를 변경하지 않고 해당 주기의 NAVIGATE 처리를 중단한다. | `NAVIGATE_FAIL` |
| Guidance 계산 실패 | `CalculateGuidance()`가 `GUIDANCE_CALCULATE_FAIL` 반환 | 새로운 `TargetCommand_t`를 생성하지 않고 기존 Target Command를 유지한다. Mission State는 `NAVIGATE`로 유지한다. | `NAVIGATE_FAIL` |
| 최종 Waypoint 도달 | `UpdateWaypointProgress()`가 `FINAL_WAYPOINT_REACHED` 반환 | Guidance 계산을 수행하지 않고 Mission State를 `MISSION_COMPLETE`로 전이한다. | `NAVIGATE_COMPLETE` |
| 정의되지 않은 Mission State | `INITIALIZE / NAVIGATE / MISSION_COMPLETE` 이외의 상태값 확인 | 상태별 처리를 수행하지 않고 실패 상태를 반환한다. | `MISSION_PROCESS_FAIL` |