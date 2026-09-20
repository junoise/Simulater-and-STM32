# STM32 소프트웨어 아키텍처

## 1. 목적
  본 문서는 STM32에서 수행되는 임무 소프트웨어 구조와 각 소프트웨어 모듈의 역할을 정의하는 것을 목적으로 한다.

  STM32 소프트웨어는 다음 기능을 수행한다.
  
  1. unity Simulater로 부터 수신한 데이터의 값이 유효한지 검사한다.
  2. INITIALIZE/NAVIGATE/Mission_COMPELETE 임무상태를 관리 한다.
  3. unity Simulate로 부터 받은 최종목적지를 기반으로 Waypoint 목록를 생성한다.
  4. 생성된 Waypoint 목록과 현재 목표 waypoint 관리한다.
  5. 현재 위치를 기반으로 목표 Waypoint 도달 여부를 판단한다.
  6. 현재 위치와 목표 Waypoint를 기반으로 Guidance를 계산한다.
  7. 연료상태, 데이터 상태 및 통신 상태 등 시스템의 상태를 판단한다.
  8. Guidance 결과와 시스템 상태 등 Unity Simulater에서 요구하는 데이터를 송신한다.

## 2. 소프트웨어 아키텍처 Overview

                    Unity Simulator                                      System Monitor          
                         ↓                                             ↙      ↓       ↘
                    Communication                             Comm Status             Fuel Status
                         ↓                                              \      |       /
                     Data Manager                                         System Status
                         ↓
                   Mission Manager
                  ↙              ↘
         Waypoint Manager       Guidance
                                   ↓
                              Output Data
                                   ↓
                             Communication
                                   ↓
                            Unity Simulator

    
## 3. 소프트웨어 모듈

### 3.1 Communication

  - 입력
    Unity Simulater에서 전송한 데이터
    Unity Simulater에 전송할 Output Data
    
  - 처리
    수신 데이터 파싱
    수신 데이터 전달
    송신 패킷 생성
    Unity Simulater로 송신
    
  - 출력
    Data Manger로 보내는 전달할 데이터
    Unity Simulater로 송신할 패킷
  
  - 내부 상태
    통신 연결 상태
    수신 성공 여부
    송신 성공 여부 
 
### 3.2 Data Manager

  - 입력
    Communication 모듈로 부터 전달받은 수신 데이터
  
  - 처리
    수신 데이터의 유효 범위를 검사
    유효한 항공기 상태 데이터 저장
    
  - 출력
    검증된 항공기 상태 데이터
    데이터 유효성 상태
  
  - 내부 상태
    데이터 유효성상태
    현재 항공기 상태 데이터
    
### 3.3 Mission Manager

  - 입력
    MissionInput_t
    SystemStatus_t
  
  - 처리
    현재 Mission State를 확인한다.
    현재 Mission State에 따라 필요한 모듈을 호출한다.
    INITIALIZE 완료 시 NAVIGATE로 전이한다.
    최종 Waypoint 도달 시 MISSION_COMPLETE로 전이한다.
    
  - 출력
    현재 State를 출력
  
  - 내부 상태
    - Mission State
      - INITIALIZE
      - NAVIGATE
      - MISSION_COMPLETE
  
### 3.4 Waypoint Manager

  - 입력
    최종목적지
    현재 위치
  
  - 처리
    최종목적지를 기반으로 waypoint 목록을 생성한다.
    현재 목표 waypoint 도달 여부를 확인한다.
    현재 목표 waypoint에 도달한 경우 다음 Waypoint로 전환
    최종 waypoint 도달 여부 판단
    
  - 출력
    현재 목표 waypoint index
    목표 waypoint list
    최종 waypoint 도달 여부
     waypoint_list_updated
    
  - 내부 상태
    waypoint 목록
    현재 waypoint index
    
### 3.5 Guidance

   - 입력
    현재 목표 waypoint
    현재 기체 상태

    - 속도
    - 위도
    - 경도
    - 고도
    - Heading
  
  - 처리
    현재 기체 상태와 목표 Waypoint를 기반으로 Guidance를 계산한다.
    목표 Heading을 계산한다.
    목표 Altitude를 계산한다.
    목표 Speed를 계산한다.
    
  - 출력
    목표 Heading
    목표 Altitude
    목표 Speed
    
  - 내부 상태
    없음
    
### 3.6 System Monitor

  - 입력
    Monitor Task가 GetCommunicationStatus()로 조회한 수신 통신 상태
    Monitor Task가 GetCurrentFuel()로 조회한 마지막 정상 연료량

  - 처리
    Monitor Task에서만 내부 시스템 상태를 갱신한다.
    Communication 상태를 저장하고, 검증된 연료량을 기준값과 비교한다.
    통신 타임아웃은 Communication이 판단하고 데이터 값의 유효성은 Data Manager가 판단한다.

  - 출력
    SystemStatus_t (communication_status, fuel_status)
    Monitor Task → System Status Queue → Mission Task로 전달한다.

  - 내부 상태
    Latest SystemStatus_t
    최초 연료 판단 전에는 Monitor Task에서 Queue 전달을 보류한다.
    COMM_OK는 구조체 0 초기화로 가정하지 않는다. 최초 정상 수신 전은 COMM_ERROR다.

### 3.7 OutputData Manager
  - 입력
    - Guidance의 target_altitude
    - Guidance의 target_heading
    - Guidance의 target_speed
    - Waypoint Manager의 waypoint_list
    - Waypoint Manager의 current_waypoint_index
    - Mission Manager의 mission_state
    - Data Manager의 data_status
    - fuel_status는 1차 S2U 출력에서 제외하며 Mission 판단용 SystemStatus_t에 유지한다.
  
  - 처리
    각 모듈에서 전달받은 출력 데이터를 저장한다.
    주기 송신 데이터와 변경 시 송신 데이터를 구분하여 관리한다.
    Waypoint List 생성 또는 변경 시 Waypoint List 변경 상태를 기록한다.
    Communication 모듈이 송신할 수 있도록 출력 데이터를 구성한다.
    
  - 출력
    주기 송신 Output Data
    Waypoint List 변경 데이터
    
  - 내부 상태
    - target_altitude
    - target_heading
    - target_speed
    - waypoint_list
    - current_waypoint_index
    - mission_state
    - data_status
    - waypoint_list_updated
    
## 4. 데이터 흐름

  ### 데이터의 흐름
  Unity Simulator
→ Comm RX Task
→ Communication
→ 패킷 파싱
→ Data Manager
→ 데이터 유효성 검사
→ Aircraft State Queue
→ Mission Task
→ Mission Manager
→ Waypoint Manager
→ Guidance
→ Output Data Manager
→ Comm TX Task
→ Communication
→ Unity Simulator 
  

## 5. FreeRTOS Task 아키텍처

STM32 소프트웨어는 각 기능의 실행 주기와 실시간성을 분리하기 위해
FreeRTOS 기반의 Task 구조로 구성한다.

각 Task는 독립적인 실행 주기와 우선순위를 가지며,
Task 간 데이터는 Queue 또는 공유 데이터 구조를 통해 전달한다.

### 5.1 Comm RX Task

- 역할
  - Unity Simulator로부터 데이터 수신
  - 수신 패킷 파싱
  - Data Manager를 통한 데이터 유효성 검사
  - 유효한 최신 Aircraft State 갱신

- 실행 주기
  - TBD

- 우선순위
  - High 후보

- 설계 근거
  - Mission Task가 최신 기체 상태를 기반으로 Guidance를 계산할 수 있도록
    수신 지연을 최소화할 필요가 있다.
  - 오래된 Aircraft State가 누적되지 않도록 최신 데이터 우선 정책을 적용한다.

### 5.2 Mission Task

- 역할
  - Mission Manager 실행
  - Waypoint Manager 실행
  - 현재 목표 Waypoint 갱신
  - Guidance 계산
  - Output Data Manager 갱신

- 실행 주기
  - TBD

- 우선순위
  - Medium - High 후보

- 설계 근거
  - Guidance 계산은 일정 주기로 수행되어야 한다.
  - 단, 최신 Aircraft State 확보가 선행되어야 하므로 Comm RX Task보다
    낮은 우선순위를 가진다.

### 5.3 Comm TX Task

- 역할
  - Output Data Manager에서 송신 데이터를 획득
  - Communication 모듈의 송신 기능을 이용하여 Unity Simulator로 데이터 송신

- 실행 주기
  - TBD

- 우선순위
  - Medium  후보

- 설계 근거
  - Guidance 결과와 상태 정보는 주기적으로 Unity Simulator에 전달되어야 한다.
  - 수신 및 Mission 처리보다 높은 우선순위를 요구하지 않는다.

### 5.4 Monitor Task

- 역할
  - 현재 연료량 확인
  - Fuel Status 판단
  - GetCommunicationStatus() 및 GetCurrentFuel() 조회
  - 시스템 이상 상태 감시

- 실행 주기
  - TBD

- 우선순위
  - Low 후보

- 설계 근거
  - 연료 상태 및 시스템 상태는 Comm RX와 Guidance 계산보다
    높은 주기의 실시간 처리가 필요하지 않는다.

### 5.5 Task 간 데이터 흐름 개요

- Comm RX Task → Mission Task
  - 검증된 최신 Aircraft State 전달
  - Aircraft State는 최신 데이터 우선 정책을 적용한다.

- Mission Task → Comm TX Task
  - Output Data 전달
  - target_heading
  - target_altitude
  - target_speed
  - current_waypoint_index
  - mission_state
  
- Mission Task / OutputData Manager
  - Data Manager의 GetDataStatus()를 조회하여 송신 OutputData_t에 반영한다.
- Comm TX Task
  - TX Queue로 받은 TxMessage_t의 패킷 생성·송신을 담당한다.
- Monitor Task → Mission Task
  - 통신 상태와 정상 연료량을 조회해 System Monitor를 갱신한다.
  - GetSystemStatus()로 얻은 SystemStatus_t를 기존 System Status Queue로 전달한다.
  - 최초 유효 연료량으로 판단하기 전에는 전달을 보류한다.

### 5.6 Task 우선순위 및 주기

Task의 최종 실행 주기와 우선순위는 구현 및 시험 과정에서 측정한
처리 시간과 요구 실시간성을 기반으로 결정한다.

| Task | 주기 | 우선순위 | 근거 |
|---|---|---|---|
| Comm RX Task | TBD | High 후보 | 최신 기체 상태 확보가 Guidance 계산의 선행 조건 |
| Mission Task | TBD | Medium - High 후보 | 수신된 상태를 기반으로 Guidance를 일정 시간 내 계산 해야함 |
| Comm TX Task | TBD | Medium 후보 | 계산된 목표값을 Unity에 주기적으로 전달 해야하지만 최신 상태 확보/계산이 우선 |
| Monitor Task | TBD | Low 후보 | 연료/상태 변화는 상대적으로 느리고 수 ms 단위 처리가 필요하지 않음 |

task 우선순위는 초기 설계값이며, 구현 후 각 task의 실행시간과 주기 충족 여부를 측정하여 최종 확정한다.

### 5.7 Task간 데이터 전달 정책 상세
- Comm RX Task → Mission Task
  - 검증된 Aircraft State를 전달한다.
  - Aircraft State는 최신 데이터가 중요하므로 길이 1의 Queue를 사용한다.
  - 새로운 데이터 수신 시 기존 데이터를 덮어쓰는 최신값 우선 정책을 적용한다.
  - Mission Task는 Queue에서 최신 Aircraft State를 읽어 사용한다.
  - Mission Task가 데이터를 읽으면 해당 Queue 항목을 제거하는 소비 방식(Receive)을 적용한다.
  - 새로운 Aircraft State가 없는 경우 Mission Task는 동일 데이터를 반복 처리하지 않는다.

- Mission Task → Comm TX Task
  - Mission Task에서 생성한 최신 Output Data를 전달한다.
  - Output Data는 최신 데이터가 중요하므로 길이 1의 Queue를 사용한다.
  - 새로운 Output Data 생성 시 기존 데이터를 덮어쓰는 최신값 우선 정책을 적용한다.
  - Comm TX Task는 Queue에서 최신 Output Data를 읽어 사용한다.
  - Comm TX Task가 데이터를 읽어도 Queue 항목을 유지하는 비소비 방식(Peek)을 적용한다.
  - 새로운 Output Data가 생성되지 않은 경우 마지막으로 생성된 Output Data를 반복 송신한다.

- Monitor Task → Mission Task
  - SystemStatus_t를 길이 1 System Status Queue로 전달하며 최신 상태 우선 정책을 적용한다.
  - 최초 상태 수신 전에는 Mission 처리를 시작하지 않는다. 초기 목적지·시작 명령을 포함한 입력은 소실되지 않도록 보존한다.
  - 최초 상태 이후 새 값이 없으면 마지막 상태를 사용한다. UART 단절 시에도 Monitor Task는 COMM_ERROR 갱신을 계속한다.
  - 최초 연료 판단 이후 연료 조회 실패 시 기존 fuel_status를 유지하고 최신 통신 상태를 전달한다.
  - SystemStatus_t에 연료 UNKNOWN enum을 추가하지 않는다. 미준비 여부는 Monitor Task의 준비 표시로 관리한다.

- Waypoint List 변경 데이터
  - Waypoint List는 초기 생성 또는 변경 시에만 전송한다.
  - OutputData Manager는 Waypoint List 변경 여부를 waypoint_list_updated 상태로 관리한다.
  - Comm TX Task는 waypoint_list_updated가 TRUE인 경우 Waypoint List를 송신한다.
  - 송신 완료 후 waypoint_list_updated를 FALSE로 갱신한다.
  - current_waypoint_index는 주기 송신 데이터에 포함한다.

  ### 오류 흐름
  - Communication Error
  - 해당 주기의 수신/처리를 중단한다.
  - Unity Simulator는 일정 시간 동안 STM32 응답이 없는 경우 Timeout으로 판단한다.

- Data Error
  - Data Manager에서 Data Status를 INVALID로 설정한다.
  - Mission Task는 해당 주기의 Waypoint 및 Guidance 처리를 수행하지 않는다.
  - Comm TX Task는 마지막 정상 Output Data와 최신 Data Status를 Unity Simulator로 송신한다.

- Fuel Low
  - System Monitor에서 Fuel Status를 LOW로 설정한다.
  - 1차 구현에서는 정상 임무 흐름을 유지한다. fuel_status는 내부 SystemStatus_t로 제공하며 Unity 주기 출력에는 포함하지 않는다.
  - 2차 구현에서는 Fuel Low 상황에 대응하는 긴급 임무 로직을 수행한다.
  - Fuel Low에 따른 비상 임무 처리는 2차 범위다.
___________________________________________________________________________________ 
주기 패킷: target_*, current_waypoint_index, mission_state, data_status
이벤트 패킷: waypoint_list
waypoint_list_updated == TRUE일 때만 이벤트 패킷 송신
Unity는 Packet Type 보고 두 형식을 구분

## 6. 통신·연료 상태 연계 보완

- Communication에 GetCommunicationStatus(), Data Manager에 GetCurrentFuel() 조회 기능을 추가한다. 기존 GetDataStatus()의 역할은 변경하지 않는다.
- 정상 패킷의 완전한 수신·형식 검사 성공 시각으로 수신 타임아웃을 판단한다. 값 범위 오류와 통신 오류는 구분한다.
- Communication과 Data Manager는 System Monitor의 갱신 함수를 직접 호출하지 않는다. Monitor Task가 getter 결과를 전달한다.
- 공유 수신 기록 및 정상 연료량의 갱신·조회 보호 방식은 TBD다. getter 또는 static 사용만으로 동기화가 해결된 것으로 보지 않는다.
- COMM_TIMEOUT_MS와 FUEL_LOW_THRESHOLD는 TBD다.
- Mission Task의 RX 무기한 대기로 시스템 오류 확인이 정지하지 않도록 상태 확인 주기와 대기 방식을 구현 시 확정한다.
- Queue 덮어쓰기와 TX 반복 송신은 설계 정책이다. 실제 CMSIS-RTOS API 구현 및 일회성 이벤트 보존과 대조해야 하며, 현재 코드에서 이미 구현된 것으로 간주하지 않는다.