## System Monitor 상세설계

### 1. 목적
- 시스템 상태를 모니터링하고 Communication / Fuel 상태를 관리한다.

### 2. 사용 데이터

#### 입력 데이터
- Communication Status
- AircraftState_t.current_fuel

#### 출력 데이터
- System Status_t
    - communication_status
    - fuel_status

#### 내부 상태
-  Latest SystemStatus_t

### 3. 함수 설계

#### 3.1 UpdateCommunicationStatus
- 목적 : Communication 모듈의 현재 통신 상태를 System Monitor 내부 상태에 갱신한다.
- 호출 Task : Monitor Task
- 입력 : Communication Status
- 출력 : 없음
- 반환값 :
    - COMM_STATUS_UPDATE_SUCCESS
    - COMM_STATUS_UPDATE_FAIL
- 처리 절차 :
    1. Communication 모듈로부터 현재 Communication Status를 전달받는다.
    2. 입력된 상태가 COMM_OK 또는 COMM_ERROR인지 확인한다.
    3. 정상적인 상태값인 경우 Latest SystemStatus_t.communication_status를 갱신한다.
    4. 갱신이 완료되면 COMM_STATUS_UPDATE_SUCCESS를 반환한다.
- 오류 처리 :
    - 정의되지 않은 Communication Status가 입력된 경우 내부 상태를 갱신하지 않는다.
    - 기존의 Latest SystemStatus_t.communication_status를 유지한다.
    - COMM_STATUS_UPDATE_FAIL을 반환한다.


#### 3.2 CheckFuelStatus
- 목적 : 현재 연료량을 기준값과 비교하여 Fuel Status를 판단하고 Latest SystemStatus_t.fuel_status를 갱신한다.
- 호출 Task : Monitor Task
- 입력 : AircraftState_t.current_fuel
- 출력 : 없음
- 반환값 :
    - FUEL_NORMAL
    - FUEL_LOW
- 처리 절차 :
    1. 현재 current_fuel 값을 확인한다.
    2. 현재 연료량을 Fuel Low 판단 기준값(FUEL_LOW_THRESHOLD, TBD)과 비교한다.
    3. 기준값보다 높은 경우 Latest SystemStatus_t.fuel_status를 FUEL_NORMAL로 갱신한다.
    4. 기준값 이하인 경우 Latest SystemStatus_t.fuel_status를 FUEL_LOW로 갱신한다.
    5. 갱신된 Fuel Status를 반환한다.
- 오류 처리 :
    - current_fuel 값 자체의 유효성 검사는 Data Manager에서 수행하므로 System Monitor에서는 별도의 데이터 범위 오류 처리를 수행하지 않는다.

#### 3.3 GetSystemStatus()
- 목적 : System Monitor가 관리하고 있는 최신 SystemStatus_t를 제공한다.
- 호출 Task : Monitor Task
- 입력 :
   없음
- 출력 : SystemStatus_t
- 반환값 :
    없음
- 처리 절차 :
    1. 내부에 저장된 Latest SystemStatus_t를 확인한다.
    2. 현재 Latest SystemStatus_t를 반환한다.
- 오류 처리 :
    - 정의되지 않은 Communication Status 또는 Fuel Status가 입력된 경우 Latest SystemStatus_t를 갱신하지 않는다.
    - 기존의 마지막 정상 SystemStatus_t를 유지한다.
    - SYSTEM_STATUS_UPDATE_FAIL을 반환한다.

### 4. 처리 흐름

    1. Monitor Task가 주기에 따라 실행된다.
    2. Communication 모듈의 최신 Communication Status를 확인하고 UpdateCommunicationStatus()를 호출하여 내부 상태를 갱신한다.
    3. 최신 AircraftState_t.current_fuel 값을 확인하고 CheckFuelStatus()를 호출하여 Fuel Status를 판단한다.
    4. 갱신된 Latest SystemStatus_t를 Mission Manager가 사용할 수 있도록 제공한다.

### 5. FreeRTOS 연계
- 사용 Task : Monitor Task
- 사용 Queue : System Status Queue : 길이 1
- Queue 전달 데이터 : SystemStatus_t
- Queue Full 시 처리 :
    - 기존 상태값을 최신 SystemStatus_t로 덮어쓴다.
    - 항상 가장 최근의 시스템 상태를 유지한다.
- Task 간 공유 자원 : 
    - Communication Status
    - Latest Aircraft State.current_fuel

### 6. 오류 처리

| 오류 상황 | 판단 기준 | 처리 방법 | 상태값 |
|---|---|---|---|
| Communication Error | Communication 모듈에서 COMM_ERROR 상태가 전달됨 | Latest SystemStatus_t.communication_status를 오류 상태로 갱신하고, 최신 SystemStatus_t를 Mission Manager에 전달한다. | COMM_ERROR |
| Fuel Low | current_fuel 값이 FUEL_LOW_THRESHOLD 이하로 내려감 | Latest SystemStatus_t.fuel_status를 LOW 상태로 갱신하고, 최신 SystemStatus_t를 Mission Manager에 전달한다. | FUEL_LOW |
| Communication 정상 복구 | 이전 COMM_ERROR 상태 이후 COMM_OK 상태가 전달됨 | communication_status를 정상 상태로 복구하고 최신 SystemStatus_t를 Mission Manager에 전달한다. | COMM_OK |
| Fuel 정상 복구 | 이전 FUEL_LOW 상태 이후 연료량이 정상 기준 범위로 복구됨 | fuel_status를 정상 상태로 복구하고 최신 SystemStatus_t를 Mission Manager에 전달한다. | FUEL_NORMAL |