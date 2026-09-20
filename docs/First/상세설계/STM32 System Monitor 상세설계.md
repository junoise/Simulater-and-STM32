## System Monitor 상세설계

### 1. 목적
- 시스템의 Communication / Fuel 상태를 관리하고 SystemStatus_t를 제공한다.
- 기존 Monitor Task와 System Status Queue를 사용한다. 새 모듈이나 Queue를 추가하지 않는다.

### 2. 사용 데이터

#### 입력 데이터
- Monitor Task가 Communication의 GetCommunicationStatus()로 얻은 CommunicationStatus_t
- Monitor Task가 Data Manager의 GetCurrentFuel() 조회에 성공하여 얻은 마지막 정상 연료량

#### 출력 데이터
- SystemStatus_t: communication_status, fuel_status
- GetSystemStatus()는 구조체 값의 복사본을 반환한다.

#### 내부 상태 및 최초 수신 정책
- Latest SystemStatus_t는 Monitor Task에서만 갱신·조회한다.
- communication_status의 초기값은 COMM_ERROR로 둔다. 최초 정상 패킷 수신 전에는 COMM_OK로 간주하지 않는다.
- 기존 FuelStatus_t의 FUEL_NORMAL/FUEL_LOW를 유지한다. 미수신을 연료 0 또는 정상 연료로 해석하지 않는다.
- Monitor Task는 최초 연료 판단 완료 여부를 별도 bool로 관리한다. 초기값 false이며 첫 GetCurrentFuel() 성공과 CheckFuelStatus() 수행 후 true로 설정한다.
- 최초 유효 연료 확보 전에는 System Status Queue로 상태를 전달하지 않는다. GetSystemStatus()의 미준비 연료 필드를 외부 판단에 사용하지 않는다.
- 최초 연료 확보 후에는 매 Monitor 주기에 통신 상태를 갱신하고, 연료 조회에 성공한 경우에만 연료 상태를 갱신한다. 이후 연료 조회가 실패해도 기존 연료 상태를 유지하며 COMM_ERROR 전달을 중단하지 않는다.
- 이 bool은 Monitor Task의 전달 준비 상태이며 SystemStatus_t에 새 필드를 추가하지 않는다.

### 3. 함수 설계

#### 3.1 UpdateCommunicationStatus
- 목적: 전달받은 통신 상태를 내부에 저장한다.
- 호출 Task: Monitor Task
- 입력: CommunicationStatus_t
- 출력: 없음
- 반환값: COMM_STATUS_UPDATE_SUCCESS / COMM_STATUS_UPDATE_FAIL
- 처리 절차:
    1. 입력이 COMM_OK 또는 COMM_ERROR인지 확인한다.
    2. 정의된 값이면 Latest SystemStatus_t.communication_status에 저장하고 COMM_STATUS_UPDATE_SUCCESS를 반환한다.
    3. 정의되지 않은 값이면 기존 상태를 유지하고 COMM_STATUS_UPDATE_FAIL을 반환한다.
- COMM_ERROR도 유효한 상태값이므로 저장 성공 시 UPDATE_SUCCESS다. 통신 정상 여부와 함수 수행 성공 여부를 구분한다.
- 타임아웃은 Communication에서 판단한다. 이 함수는 중복 계산하지 않는다.

#### 3.2 CheckFuelStatus
- 목적: 검증된 연료량과 기준값을 비교하여 연료 상태를 갱신한다.
- 호출 Task: Monitor Task
- 호출 조건: GetCurrentFuel() 조회 성공
- 입력: current_fuel (0~100 범위의 유한한 값)
- 출력: 내부 Latest SystemStatus_t.fuel_status 갱신
- 반환값: FuelStatus_t (FUEL_NORMAL / FUEL_LOW)
- 처리 절차:
    1. 연료량을 FUEL_LOW_THRESHOLD와 비교한다.
    2. 기준값보다 높으면 FUEL_NORMAL로 저장한다.
    3. 기준값 이하이면 FUEL_LOW로 저장한다.
    4. 저장한 FuelStatus_t를 반환한다.
- FUEL_LOW_THRESHOLD 수치는 TBD다. 임시값을 쓰면 시험용임을 표시한다.
- 연료량의 범위·유한성 검사는 Data Manager의 책임이며 이 함수의 선행 조건이다.
- 1차에서는 FUEL_LOW 상태를 제공하되, 이를 근거로 비상 임무로 전환하지 않는다.

#### 3.3 GetSystemStatus
- 목적: 최신 시스템 상태의 복사본을 제공한다.
- 호출 Task: Monitor Task
- 입력: 없음
- 출력 인자: 없음
- 반환값: SystemStatus_t
- 처리 절차: 내부 Latest SystemStatus_t를 값으로 반환한다.
- 내부 상태를 갱신하지 않으며 SYSTEM_STATUS_UPDATE_FAIL 등의 갱신 결과를 반환하지 않는다.
- Monitor Task는 최초 연료 판단 완료 후에 반환값을 Queue 전달용으로 사용한다.

### 4. 처리 흐름

1. Monitor Task가 주기적으로 실행된다.
2. GetCommunicationStatus()를 호출하고 결과를 UpdateCommunicationStatus()에 전달한다.
3. GetCurrentFuel()을 호출한다. 성공한 경우 CheckFuelStatus()를 호출하고 연료 판단 완료를 기록한다.
4. 아직 연료 판단을 한 적이 없다면 해당 주기의 Queue 전달을 생략한다.
5. 준비된 상태라면 GetSystemStatus()로 복사본을 얻고 System Status Queue에 전달한다. 갱신 함수에 정의되지 않은 통신 값이 들어온 경우는 내부 연계 오류로 취급하며 정상 갱신으로 간주하지 않는다.
6. Mission Task가 Queue의 최신 상태를 확인하고 ProcessMission()에 전달한다.
7. 최초 준비 이후에는 UART 입력이 끊겨도 Monitor Task가 계속 실행되어 COMM_ERROR를 전달한다.

### 5. FreeRTOS 연계

- 실행 Task: Monitor Task
- 사용 Queue: 기존 System Status Queue, 길이 1, 자료형 SystemStatus_t
- Producer: Monitor Task / Consumer: Mission Task
- 최신 상태 우선 정책을 유지하되, 일반 osMessageQueuePut()만으로 덮어쓰기가 구현된 것으로 간주하지 않는다. 실제 Queue Full 처리 및 반환값 확인은 Task 연결 시 구현한다.
- 최초 상태를 받기 전 Mission Task는 임무 처리를 시작하지 않는다. 대기할 때 CPU를 점유하는 반복 확인을 피하고 Monitor Task가 실행될 수 있도록 block 또는 yield한다.
- 최초 수신 이후 새 상태가 없으면 마지막 상태를 유지하되, Monitor Task의 정상 주기 실행을 전제로 한다. Monitor 자체 정지 감지 정책은 별도 미정이다.
- Communication 및 Data Manager 내부 데이터는 해당 모듈의 getter로만 얻는다. 이 데이터의 갱신·조회 동시 접근 보호 방식은 TBD다.
- Latest SystemStatus_t는 다른 Task가 직접 접근하지 않고 Queue 복사본으로 전달한다.

### 6. 오류 및 복구 처리

| 상황 | 판단 기준 | 처리 | 상태/결과 |
|---|---|---|---|
| 최초 수신 전 | 정상 패킷 수신 이력 없음 | 통신 정상으로 판정하지 않음 | COMM_ERROR |
| 최초 연료 미확보 | GetCurrentFuel 실패, 연료 판단 이력 없음 | Queue 전달 보류, Mission 최초 상태 대기 | 미준비 |
| 통신 단절 | 마지막 정상 수신 후 타임아웃 | 최초 준비 이후에는 기존 연료 상태와 함께 오류 상태 전달 | COMM_ERROR |
| 통신 복구 | 새 정상 패킷 수신, 타임아웃 미만 | 정상 상태 갱신·전달 | COMM_OK |
| 잘못된 통신 enum | COMM_OK/COMM_ERROR 이외 | 기존 내부 상태 유지, 갱신 실패 반환 | COMM_STATUS_UPDATE_FAIL |
| 연료 부족 | current_fuel <= FUEL_LOW_THRESHOLD | 연료 상태 갱신, 1차 정상 임무 정책 유지 | FUEL_LOW |
| 연료 정상 복구 | current_fuel > FUEL_LOW_THRESHOLD | 연료 상태 갱신 | FUEL_NORMAL |
| 연료 조회 실패 | GetCurrentFuel이 false | 새 연료 판단 생략. 최초 판단 이후에는 기존 연료 상태와 최신 통신 상태를 전달 | 기존 fuel_status 유지 |

### 7. 미정 및 구현 확인 항목

- COMM_TIMEOUT_MS, 사용할 시간 API 및 tick 환산
- FUEL_LOW_THRESHOLD
- Communication 수신 기록과 Data Manager 정상 연료량의 동시 접근 보호 방식
- Monitor 주기와 상태 전파 지연 검증
- Mission Task가 RX Queue에서 무기한 대기 중이어도 System Status Queue의 통신 오류를 확인할 수 있는 대기 방식. 새 항공기 입력 없이 Guidance를 반복 실행하지 않는 정책은 유지한다.
- 현재 코드의 임시 COMM_OK와 시험용 연료값은 실제 상태 공급 연결 시 교체한다. 본 문서는 설계이며 코드 구현 완료를 뜻하지 않는다.