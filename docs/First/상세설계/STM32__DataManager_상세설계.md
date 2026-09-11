## Data Manager 상세설계

### 1. 목적
- Communication 모듈에서 파싱된 RxMessage_t의 데이터 유효성을 검사하고,
  유효한 최신 데이터를 내부 상태에 저장한 뒤 MissionInput_t 형태로 RX Queue에 전달한다.

### 2. 사용 데이터

#### 입력 데이터
- RxMessage_t
    - AircraftState_t
    - Destination_t
    - destination_valid
    - mission_start_command
    - mission_command_valid

#### 출력 데이터
- MissionInput_t
    - AircraftState_t
    - Destination_t
    - destination_valid
    - mission_start_command
    - mission_command_valid

#### 내부 상태
- Latest Aircraft State
- Destination Data
- Mission Start Command
- Data Status
- Aircraft State Valid Status
- Destination Valid Status
- Mission Command Valid Status

---

### 3. 함수 설계

#### 3.1 ValidateAircraftState
- 목적 : AircraftState가 정상적인 범위에 있는지 검사하는 역할
- 호출 Task : Comm Rx task
- 입력 : AircraftState_t
- 출력 : 없음
- 반환값 :
    - AIRCRAFT_SUCCESS
    - AIRCRAFT_FAIL
- 처리 절차 :
    1. 입력받은 AircraftState_t의 각 필드를 확인한다.
    2. 각 데이터가 정상적인 범위에 있는지 검사한다.
    3. 모든 데이터가 정상 범위인 경우 AIRCRAFT_SUCCESS를 반환한다
    4. 하나 이상의 데이터가 정상 범위를 벗어난 경우 AIRCRAFT_FAIL을 반환한다.
- 오류 처리 :
    - 하나 이상의 데이터가 정상 범위를 벗어난 경우 해당 Aircraft State는 유효하지 않은 데이터로 판단한다.
    - 유효하지 않은 Aircraft State는 최신 정상 데이터로 갱신하지 않는다.
    - Aircraft State Valid Status를 실패 상태로 갱신한다.

#### 3.2 ValidateDestination
- 목적 : Destination_t가 정상적인 범위에 있는지 검사하는 역할
- 호출 Task : Comm Rx task
- 호출 조건 : destination_valid == true인 경우에만 호출한다.
- 입력 : Destination_t
- 출력 : 없음
- 반환값 :
    - DESTINATION_SUCCESS
    - DESTINATION_FAIL
- 처리 절차 :
    1. Destination_t의 각 필드를 확인한다.
    2. 각 데이터가 정상적인 범위에 있는지 검사한다.
    3. 모든 데이터가 정상 범위인 경우 DESTINATION_SUCCESS를 반환한다.
    4. 하나 이상의 데이터가 정상 범위를 벗어난 경우 DESTINATION_FAIL을 반환한다.
- 오류 처리 :
    - 하나 이상의 데이터가 정상 범위를 벗어난 경우 해당 Destination 는 유효하지 않은 데이터로 판단한다.
    - 유효하지 않은 Destination는 최신 정상 데이터로 갱신하지 않는다.
    - Destination Valid Status를 실패 상태로 갱신한다.
#### 3.3 ValidateMissionCommand
- 목적 : mission_command가  정상적인 범위에 있는지 검사하는 역할
- 호출 Task :Comm Rx task
- 호출 조건 : mission_command_valid == true인 경우에만 호출한다.
- 입력 :mission_start_command
- 출력 :없음
- 반환값 : 
    - MISSION_COMMAND_SUCCESS
    - MISSION_COMMAND_FAIL
- 처리 절차 :
    1. mission_start_command가 정상적인 범위에 있는지 검사한다.
    2. mission_start_command가 값이 0 또는 1인 경우 MISSION_COMMAND_SUCCESS를 반환한다.
    3. 그 외 경우 MISSION_COMMAND_FAIL을 반환한다.
- 오류 처리 :
    - 데이터가 정상 범위를 벗어난 경우 해당 Mission Command 는 유효하지 않은 데이터로 판단한다.
    - 유효하지 않은 Mission Command는 최신 정상 데이터로 갱신하지 않는다.
    - Mission Command Valid Status를 실패 상태로 갱신한다.

#### 3.4 UpdateData
- 목적 : 유효성 검사를 통과한 데이터를 Data Manager의 내부 상태에 최신 값으로 저장한다.
- 호출 Task : Comm Rx task
- 입력 : AircraftState_t,Destination_t,mission_start_command,각 데이터의 유효성 검사 결과
- 출력 :없음
- 반환값 :
    - UPDATE_SUCCESS
    - UPDATE_FAIL
- 처리 절차 :
    1. AircraftState_t가 유효한 경우 Latest Aircraft State를 최신 값으로 갱신한다.
    2. Destination_t가 유효하고 새로운 목적지 데이터가 존재하는 경우 Destination Data를 갱신한다.
    3. Mission Command가 유효하고 새로운 명령이 존재하는 경우 Mission Start Command를 갱신한다.
    4. 각 데이터의 유효성 상태를 최신 검사 결과로 갱신한다.
    5. 내부 상태 갱신이 정상적으로 완료되면 UPDATE_SUCCESS를 반환한다.
- 오류 처리 :

#### 3.5 CreateMissionInput
- 목적 :  유효성 검사를 통과한 최신 데이터를 기반으로 MissionInput_t를 생성하고 RX Queue에 전달한다.
- 호출 Task :Comm Rx task
- 입력 :
    - Latest Aircraft State
    - Destination Data
    - Mission Start Command
    - 각 데이터의 유효성 상태 
- 출력 : MissionInput_t
- 반환값 :
    - INPUT_SUCCESS
    - INPUT_FAIL
- 처리 절차 :
    1. Data Manager 내부에 저장된 최신 정상 데이터를 확인한다.
    2. AircraftState_t를 MissionInput_t에 저장한다.
    3. 새로운 Destination이 유효한 경우 Destination_t와 해당 유효성 정보를 MissionInput_t에 저장한다.
    4. 새로운 Mission Command가 유효한 경우 mission_start_command와 해당 유효성 정보를 MissionInput_t에 저장한다.
    5. 완성된 MissionInput_t를 RX Queue에 전달한다.
    6. Queue 전달이 정상적으로 완료되면 INPUT_SUCCESS를 반환한다.
- 오류 처리 :
    - 필수 데이터인 Aircraft State가 유효하지 않은 경우 MissionInput_t를 생성하지 않고 INPUT_FAIL을 반환한다.
    - RX Queue 전달에 실패한 경우 INPUT_FAIL을 반환한다.
    - Destination 또는 Mission Command가 존재하지 않는 것은 오류로 판단하지 않는다.



#### 3.6  GetDataStatus

- 목적 : Data Manager가 관리 중인 최신 Data Status를 외부 모듈에 제공한다.

- 호출 Task : Mission Task 또는 OutputData Manager가 실행되는 Task

- 입력 :없음

- 출력 : 없음

- 반환값 :
    - DataStatus_t
        - DATA_UNKNOWN
        - DATA_VALID
        - DATA_INVALID

- 처리 절차 :
    1. 내부에 저장된 최신 Data Status를 확인한다.
    2. 현재 Data Status를 반환한다.

- 오류 처리 :없음
---

### 4. 데이터 처리 흐름

1. Comm RX Task가 Communication 모듈로부터 `RxMessage_t`를 전달받는다.
2. `AircraftState_t`에 대해 `ValidateAircraftState()`를 호출하여 유효성을 검사한다.
3. `destination_valid == true`인 경우 `ValidateDestination()`을 호출한다.
4. `mission_command_valid == true`인 경우 `ValidateMissionCommand()`를 호출한다.
5. 유효성 검사를 통과한 데이터만 `UpdateData()`를 통해 Data Manager 내부 최신 상태로 갱신한다.
6. `CreateMissionInput()`을 호출하여 최신 정상 데이터를 기반으로 `MissionInput_t`를 생성한다.
7. 생성된 `MissionInput_t`를 RX Queue에 전달한다.
8. Aircraft State 유효성 검사에 실패한 경우 해당 주기의 `MissionInput_t`는 생성하지 않고 오류 상태를 갱신한다.

### 5. FreeRTOS 연계

- Comm RX Task
    - Communication 모듈에서 파싱된 RxMessage_t를 전달받아 유효성 검사 및 내부 데이터 갱신을 수행한다.
    - 검증 완료 후 MissionInput_t를 생성하여 RX Queue에 전달한다.
- RX Queue : 길이 1
- Queue 전달 데이터 :MissionInput_t
- Queue Full 시 처리 : 기존 Queue 데이터를 최신 MissionInput_t로 덮어쓴다. Mission Task에는 항상 가장 최근의 정상 데이터를 전달한다.
- Task 간 공유 자원 : 없음.

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Aircraft State 유효성 실패 | latitude, longitude, altitude, heading, speed, fuel 중 하나 이상이 정의된 정상 범위를 벗어남 | 해당 Aircraft State를 내부 최신 데이터에 반영하지 않는다. 해당 주기의 MissionInput_t는 생성하지 않으며 Data Status를 오류 상태로 갱신한다. | AIRCRAFT_FAIL, DATA_INVALID |
| Destination 유효성 실패 | destination_valid == true인 상태에서 Destination의 latitude, longitude, altitude 중 하나 이상이 정상 범위를 벗어남 | 해당 Destination을 내부 상태에 반영하지 않는다. Aircraft State가 정상이라면 주기 데이터 처리는 계속 수행하되, Destination은 MissionInput_t에 유효한 데이터로 전달하지 않는다. | DESTINATION_FAIL |
| Mission Command 유효성 실패 | mission_command_valid == true인 상태에서 mission_start_command가 정의된 값 범위를 벗어남 | 해당 Mission Command를 내부 상태에 반영하지 않는다. Aircraft State가 정상이라면 나머지 데이터 처리는 계속 수행한다. | MISSION_COMMAND_FAIL |
| 정상 데이터 복구 | 이전 Aircraft State 오류 이후 정상 범위의 Aircraft State가 수신됨 | 최신 정상 Aircraft State를 내부 상태에 반영하고 Data Status를 정상 상태로 복구한다. MissionInput_t 생성을 재개한다. | DATA_VALID, AIRCRAFT_SUCCESS|