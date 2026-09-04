## OutputData Manager 상세설계

### 1. 목적
- Mission Manager 및 관련 모듈에서 생성된 데이터를
  Unity 송신용 데이터 형식으로 정리하고 TxMessage_t를 생성한다.

### 2. 사용 데이터

#### 입력 데이터
- MissionState_t
- TargetCommand_t
- WaypointList_t
- current_waypoint_index

#### 출력 데이터
- TxMessage_t
    - OutputData_t
    - waypoint_list
    - waypoint_list_valid


#### 내부 상태
- Latest TxMessage_t

---

### 3. 함수 설계

#### 3.1 UpdateOutputData
- 목적 : Mission Manager에서 전달받은 임무 출력 데이터와 각 모듈에서 조회한 상태값을 기반으로 Latest TxMessage_t.output_data를 갱신한다.
- 호출 Task : Mission Task
- 입력 :
    - MissionState_t
    - TargetCommand_t
    - current_waypoint_index
- 출력 :없음
- 반환값 :
    - OUTPUT_UPDATE_SUCCESS
    - OUTPUT_UPDATE_FAIL
- 처리 절차 :
    1. 입력받은 `MissionState_t`를 확인한다.
    2. 입력받은 `TargetCommand_t`의 `target_heading`, `target_altitude`, `target_speed`를 확인한다.
    3. 입력받은 `current_waypoint_index`를 확인한다.
    4. Data Manager의 `GetDataStatus()`를 호출하여 최신 `DataStatus_t`를 획득한다.
    5. 각 데이터를 `Latest TxMessage_t.output_data`에 저장한다.
    6. 정상적으로 갱신되면 `OUTPUT_UPDATE_SUCCESS`를 반환한다.
- 오류 처리 :
    - 입력 데이터 처리에 실패한 경우 `OUTPUT_UPDATE_FAIL`을 반환한다.
    - 갱신 실패 시 기존 `Latest TxMessage_t.output_data`를 유지한다.

#### 3.2 UpdateWaypointData
- 목적 : 새로 생성되거나 변경된 Waypoint 목록을 Latest TxMessage_t에 갱신한다.
- 호출 Task : Mission Task
- 호출 조건 : Waypoint 목록이 새로 생성되었거나 변경된 경우
- 입력 :WaypointList_t
- 출력 :없음
- 반환값 :
    - WAYPOINT_OUTPUT_UPDATE_SUCCESS
    - WAYPOINT_OUTPUT_UPDATE_FAIL
- 처리 절차 :
    1. 입력받은 WaypointList_t가 유효한지 확인한다.
    2. 유효한 Waypoint 목록을 Latest TxMessage_t.waypoint_list에 저장한다.
    3. Latest TxMessage_t.waypoint_list_valid를 true로 설정한다.
    4. 갱신이 정상적으로 완료되면 WAYPOINT_OUTPUT_UPDATE_SUCCESS를 반환한다.

- 오류 처리 :
    - 유효하지 않은 Waypoint 목록이 입력된 경우 갱신하지 않는다.
    - `waypoint_list_valid`를 true로 설정하지 않는다.
    - `WAYPOINT_OUTPUT_UPDATE_FAIL`을 반환한다.

#### 3.3 CreateTxMessage
- 목적 : 최신 Output Data와 Waypoint Data를 기반으로 TxMessage_t를 구성하고 TX Queue에 전달한다.
- 호출 Task :Mission Task
- 입력 :Latest TxMessage_t
- 출력 : TxMessage_t
- 반환값 :
    - TX_MESSAGE_SUCCESS
    - TX_MESSAGE_FAIL

- 처리 절차 :
    1. Latest TxMessage_t의 OutputData_t가 정상적으로 갱신되어 있는지 확인한다.
    2. waypoint_list_valid 값을 확인하여 Waypoint 목록 포함 여부를 판단한다.
    3. 최신 데이터를 기반으로 TxMessage_t를 구성한다.
    4. 생성된 TxMessage_t를 TX Queue에 전달한다.
    5. TX Queue 전달이 정상적으로 완료되면 TX_MESSAGE_SUCCESS를 반환한다.
    6. Waypoint 데이터가 포함되어 전송된 경우 waypoint_list_valid를 false로 초기화한다.
- 오류 처리 :
    - 유효한 OutputData_t가 존재하지 않는 경우 TxMessage_t를 생성하지 않고 TX_MESSAGE_FAIL을 반환한다.
    - TX Queue 전달에 실패한 경우 TX_MESSAGE_FAIL을 반환한다.
    - Queue 전달 실패 시 기존 Latest TxMessage_t는 유지한다.

---

### 4. 처리 흐름

1. Mission Task에서 MissionState_t, TargetCommand_t, current_waypoint_index를 전달받는다.

2. UpdateOutputData()를 호출하여 Latest TxMessage_t.output_data를 최신 값으로 갱신한다.

3. Waypoint 목록이 새로 생성되었거나 변경된 경우 UpdateWaypointData()를 호출하여 Latest TxMessage_t.waypoint_list를 갱신하고 waypoint_list_valid를 true로 설정한다.

4. CreateTxMessage()를 호출하여 최신 데이터를 기반으로 TxMessage_t를 구성한다.

5. 생성된 TxMessage_t를 TX Queue에 전달한다.

6. Waypoint 목록이 포함된 TxMessage_t가 정상적으로 TX Queue에 전달된 경우 waypoint_list_valid를 false로 초기화한다.

7. TX Queue에 전달된 TxMessage_t는 Comm TX Task에서 수신하여 송신 패킷 생성 및 UART 송신에 사용한다.

### 5. FreeRTOS 연계

- 사용 Task : Mission Task,Comm TX Task
- 사용 Queue : TX Queue : 길이 1
- Queue 전달 데이터 : TxMessage_t
- Queue Full 시 처리 :
    - 기존 데이터를 최신 TxMessage_t로 덮어쓴다.
    - 항상 가장 최근 송신 데이터를 유지한다.
- Task 간 공유 자원 :없음

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Output Data 갱신 실패 | UpdateOutputData()가 실패를 반환함 | 기존 Latest TxMessage_t.output_data를 유지하고 해당 주기의 출력 데이터 갱신을 수행하지 않는다. | OUTPUT_UPDATE_FAIL |
| Waypoint Data 갱신 실패 | UpdateWaypointData()가 실패를 반환함 | 기존 Waypoint 데이터를 유지하고 waypoint_list_valid를 true로 설정하지 않는다. | WAYPOINT_OUTPUT_UPDATE_FAIL |
| TxMessage 생성 또는 Queue 전달 실패 | 유효한 OutputData_t가 없거나 TX Queue 전달에 실패함 | TxMessage_t를 송신하지 않고 기존 Latest TxMessage_t를 유지한다. 다음 Mission 주기에 최신 데이터로 다시 시도한다. | TX_MESSAGE_FAIL |