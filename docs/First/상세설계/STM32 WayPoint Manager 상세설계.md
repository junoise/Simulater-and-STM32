## Waypoint Manager 상세설계

### 1. 목적
- 최종 목적지를 기반으로 Waypoint 목록을 생성하고,
  현재 목표 Waypoint와 Waypoint 진행 상태를 관리한다.

### 2. 사용 데이터

#### 입력 데이터
- Destination_t
- AircraftState_t

#### 출력 데이터
- 없음

#### 내부 상태
- WaypointList_t
- current_waypoint_index

---

### 3. 함수 설계

#### 3.1 GenerateWaypointList
- 목적 :최종 목적지 정보를 기반으로 Waypoint 목록을 생성한다.
- 호출 Task : Mission Task
- 호출 조건 : Mission State가 INITIALIZE 상태이고 유효한 Destination 데이터가 존재할 때 호출한다.
- 입력 : Destination_t
- 출력 : -
- 반환값 :
    - WAYPOINT_GENERATE_SUCCESS
    - WAYPOINT_GENERATE_FAIL

- 처리 절차 :
    1. 입력된 Destination_t의 목적지 좌표를 확인한다.
    2. 현재 위치와 최종 목적지를 기준으로 Waypoint 생성 규칙에 따라 중간 Waypoint를 계산한다.
    3. 계산된 Waypoint를 WaypointList_t에 순서대로 저장한다.
    4. 마지막 Waypoint에는 최종 Destination 좌표를 저장한다.
    5. current_waypoint_index를 첫 번째 Waypoint로 초기화한다.
    6. Waypoint 목록 생성이 정상적으로 완료되면 WAYPOINT_GENERATE_SUCCESS를 반환한다.
- 오류 처리 :
    - 유효한 Destination 데이터가 없는 경우 Waypoint 목록을 생성하지 않고 WAYPOINT_GENERATE_FAIL을 반환한다.
    - Waypoint 개수가 정의된 최대 개수를 초과하는 경우 WAYPOINT_GENERATE_FAIL을 반환한다.
    - 생성 실패 시 기존 WaypointList_t를 갱신하지 않는다.

#### 3.2 GetCurrentWaypoint
- 목적 : 최신 waypoint를 제공하기 위한 함수
- 호출 Task : Mission Task
- 입력 : 없음
- 출력 : Waypoint_t(현재 current_waypoint_index에 해당하는 Waypoint)
- 반환값 : 
    - true
    - false
- 처리 절차 :
    1. current_waypoint_index가 유효한 범위인지 확인한다.
    2. 유효한 경우 해당 Waypoint를 출력값에 저장한다.
    3. 정상적으로 제공한 경우 true를 반환한다.
- 오류 처리 :
    - current_waypoint_index가 유효한 범위를 벗어난 경우 유효하지 않은 Waypoint를 반환하지 않는다.
    - 필요 시 오류 상태 또는 별도 실패 코드 처리 방식을 사용한다.

#### 3.3 GetWAypointList
- 목적 : 현재 Waypoint Manager가 관리 중인 전체 Waypoint 목록을 외부 모듈에 제공한다.
- 호출 Task : Mission Task
- 입력 : 없음
- 출력 :WaypointList_t
- 반환값 :
    - true
    - false
- 처리 절차 :
    1. 내부에 저장된 WaypointList_t가 유효한지 확인한다.
    2. 유효한 경우 전체 Waypoint 목록을 출력값으로 전달한다.
    3. 정상적으로 전달한 경우 true를 반환한다.
- 오류 처리 :
    - 유효한 WaypointList_t가 존재하지 않는 경우 false를 반환한다.

#### 3.4 GetCurrentWaypointIndex()
- 목적 : 현재 Waypoint Manager가 관리 중인  Waypoint index를 외부 모듈에 제공한다.
- 호출 Task : Mission Task
- 입력 : 없음
- 출력 : 없음
- 반환값 : current_waypoint_index
- 처리 절차 :
    1. 내부에 저장된 current_waypoint_index 값을 확인한다.
    2. 현재 current_waypoint_index를 반환한다.
- 오류 처리 : 없음

#### 3.5 UpdateWaypointProgress

- 목적 :현재 Aircraft가 목표 Waypoint에 도달했는지 검사하고, 도달한 경우 다음 Waypoint로 전환하며 최종 Waypoint 도달 여부를 판단한다.
- 호출 Task : Mission Task
- 입력 :AircraftState_t
- 출력 :없음
- 반환값 :
    - WAYPOINT_IN_PROGRESS
    - FINAL_WAYPOINT_REACHED
    - WAYPOINT_CHECK_FAIL

- 처리 절차 :
    1. 현재 index의 Waypoint와 Aircraft 현재 위치 사이의 거리를 계산한다.
    2. 도달 기준 거리보다 큰 경우 WAYPOINT_IN_PROGRESS를 반환한다.
    3. Waypoint에 도달했고 현재 Waypoint가 최종 Waypoint가 아닌 경우 current_waypoint_index를 1 증가시킨다.
    4. 다음 Waypoint로 전환한 후 WAYPOINT_IN_PROGRESS를 반환한다.
    5. 현재 Waypoint가 최종 Waypoint이고 도달한 경우 FINAL_WAYPOINT_REACHED를 반환한다.
- 오류 처리 :
    - 유효한 WaypointList_t가 존재하지 않는 경우 WAYPOINT_CHECK_FAIL을 반환한다.
    - current_waypoint_index가 유효 범위를 벗어난 경우 WAYPOINT_CHECK_FAIL을 반환한다.
    - 오류 발생 시 current_waypoint_index를 변경하지 않는다.
---

### 4. 처리 흐름

    1.Mission Task에서 INITIALIZE 상태일 때 유효한 Destination 데이터를 전달받으면 GenerateWaypointList() 를 호출하여 Waypoint 목록을 생성한다.
    2. 생성된 WaypointList_t와 current_waypoint_index를 Waypoint Manager 내부 상태로 저장한다.
    3. NAVIGATE 상태에서 Mission Manager가 GetCurrentWaypoint()를 호출하여 현재 목표 Waypoint를 획득하고 Guidance에 전달한다.
    4. 매 주기 UpdateWaypointProgress()를 호출하여 현재 Aircraft 위치와 목표 Waypoint의 도달 여부를 판단한다.
    5. Waypoint에 도달한 경우 최종 Waypoint가 아니면 current_waypoint_index를 증가시키고, 최종 Waypoint인 경우 FINAL_WAYPOINT_REACHED를 반환한다.
    6. 필요 시 Mission Manager가 GetWaypointList()와 GetCurrentWaypointIndex()를 호출하여 필요한 데이터를 획득하고 OutputData Manager에 전달한다.

### 5. FreeRTOS 연계
- 사용 Task : Mission Task
- 사용 Queue : 없음
- Queue 전달 데이터 : 없음
- Queue Full 시 처리 : 없음
- Task 간 공유 자원 : 없음

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Waypoint 생성 실패 | GenerateWaypointList함수반환값 | 생성 실패 시 기존 WaypointList_t를 갱신하지 않는다.| WAYPOINT_GENERATE_FAIL |
| 현재 Waypoint 조회 실패 | 유효한 Waypoint List가 없거나 current_waypoint_index가 범위를 벗어남 | 잘못된 Waypoint를 반환하지 않고 Mission Manager에 실패 상태를 전달한다. | WAYPOINT_GET_FAIL |
| Waypoint 도달 검사 실패 | UpdateWaypointProgress() 수행 중 유효한 Waypoint가 없거나 index 오류 발생 | index와 내부 Waypoint 상태를 변경하지 않고 해당 주기의 도달 처리를 중단한다. | WAYPOINT_CHECK_FAIL |