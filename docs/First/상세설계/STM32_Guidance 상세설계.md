## Guidance 상세설계

### 1. 목적
- 현재 Aircraft 상태와 현재 목표 Waypoint를 기반으로
  목표 Waypoint에 도달하기 위한 target heading / altitude / speed를 계산한다.

### 2. 사용 데이터

#### 입력 데이터
- AircraftState_t

#### 출력 데이터
- TargetCommand_t
  - target_heading
  - target_altitude
  - target_speed

#### 내부 상태
- Latest TargetCommand_t

---

### 3. 함수 설계

#### 3.1 CalculateGuidance()

- 목적 :
  - 현재 Aircraft 상태와 현재 목표 Waypoint를 기반으로
    target heading / altitude / speed를 계산한다.

- 호출 Task :
  - Mission Task

- 입력 :
  - AircraftState_t

- 출력 :
  - TargetCommand_t

- 반환값 :
  - GUIDANCE_CALCULATE_SUCCESS
  - GUIDANCE_CALCULATE_FAIL

- 처리 절차 :
  1. 입력받은 AircraftState_t의 현재 상태 정보를 확인한다.
  2. Waypoint Manager의 GetCurrentWaypoint()를 호출하여 현재 목표 Waypoint를 획득한다.
  3. AircraftState_t와 현재 목표 Waypoint를 기반으로 target_heading을 계산한다.
  4. 현재 목표 Waypoint를 기반으로 target_altitude를 계산한다.
  5. 비행 조건 및 목표 Waypoint를 기반으로 target_speed를 계산한다.
  6. 계산된 값을 TargetCommand_t에 저장한다.
  7. 계산이 정상적으로 완료되면 Latest TargetCommand_t를 갱신한다.
  8. GUIDANCE_CALCULATE_SUCCESS를 반환한다.

- 오류 처리 :
  - GetCurrentWaypoint() 호출에 실패한 경우 GUIDANCE_CALCULATE_FAIL을 반환한다.
  - Guidance 계산에 실패한 경우 GUIDANCE_CALCULATE_FAIL을 반환한다.
  - 계산 실패 시 Latest TargetCommand_t를 갱신하지 않고 기존 값을 유지한다.

---

### 4. 처리 흐름

1. Mission Task에서 AircraftState_t를 전달받으면 CalculateGuidance()를 호출한다.
2. CalculateGuidance()는 Waypoint Manager의 GetCurrentWaypoint()를 호출하여 현재 목표 Waypoint를 획득한다.
3. AircraftState_t와 현재 목표 Waypoint를 기반으로 target_heading, target_altitude, target_speed를 계산한다.
4. 계산된 값을 TargetCommand_t에 저장한다.
5. 계산 성공 시 Latest TargetCommand_t를 갱신하고 TargetCommand_t를 Mission Manager에 제공한다.
6. 계산 실패 시 Latest TargetCommand_t는 갱신하지 않고 실패 상태를 반환한다.

### 5. FreeRTOS 연계

- 사용 Task :
  - Mission Task

- 사용 Queue :
  - 없음

- Queue 전달 데이터 :
  - 없음

- Queue Full 시 처리 :
  - 없음

- Task 간 공유 자원 :
  - 없음

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Current Waypoint 조회 실패 | GetCurrentWaypoint()가 false를 반환함 | Guidance 계산을 수행하지 않고 기존 Latest TargetCommand_t를 유지한다. | GUIDANCE_CALCULATE_FAIL |
| Guidance 계산 실패 | target heading / altitude / speed 계산 중 유효하지 않은 결과가 발생함 | Latest TargetCommand_t를 갱신하지 않고 기존 값을 유지한다. | GUIDANCE_CALCULATE_FAIL |
| Guidance 계산 성공 | Current Waypoint 조회 및 모든 목표값 계산이 정상적으로 완료됨 | 계산 결과를 Latest TargetCommand_t에 갱신하고 Mission Manager에 제공한다. | GUIDANCE_CALCULATE_SUCCESS |