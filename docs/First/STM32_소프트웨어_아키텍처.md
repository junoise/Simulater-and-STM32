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
                    Communication                             Comm Status  Data Status  Fuel/System
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
    없음
  
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
    현재 목표 waypoint (latitude,longitude,altitude)
    최종 waypoint 도달 여부
    
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
    현재 연료량
    Data Manger 의 데이터 상태
    Communication의 통신 상태
  
  - 처리
    현재 연료량을 기반으로 연료 상태를 판단한다.
    각 모듈의 상태 정보를 기반으로 전체 시스템 상태를 판단한다.
    Communication status 와 System status는 stm32 내부 판단용 변수이다.
    
  - 출력
    fuel status
    data status
  - 내부 상태    
    -Fuel status
    -Data status
    -Communication status
    -System status
    
### 3.7 OutputData Manager
  - 입력
    - Guidance의 target_altitude
    - Guidance의 target_heading
    - Guidance의 target_speed
    - Waypoint Manager의 current_waypoint_latitude
    - Waypoint Manager의 current_waypoint_longitude
    - Waypoint Manager의 current_waypoint_altitude
    - Waypoint Manager의 current_waypoint_index
    - Mission Manager의 mission_state
    - Data Manager의 data_status
    - System Monitor의 fuel_status
  
  - 처리
    각 모듈에서 전달받은 출력 데이터를 저장한다.
    Communication 모듈이 송신할 수 있도록 출력 데이터를 구성한다.
    
  - 출력
    Unity Simulator로 송신할 Output Data
    
  - 내부 상태
    - target_altitude
    - target_heading
    - target_speed
    - current_waypoint_latitude
    - current_waypoint_longitude
    - current_waypoint_altitude
    - current_waypoint_index
    - mission_state
    - data_status
    - fuel_status
    
## 4. 데이터 흐름
  ### 데이터의 흐름
  Unity Simulator -> Communication -> 수신 패킷 파싱 -> Data Manager -> 데이터 유효성 검사 -> System Monitor
  -> 시스템 상태 확인 -> Mission Manager -> WayPointManager -> 현재 목표 Waypoint 갱신 -> Guidance ->
  목표 Heading / Altitude / Speed 계산 -> Output Data Manager -> 송신 데이터 구성 -> Communication -> Unity Simulator 
  ### 오류 흐름
  Communication Error -> 해당 주기 처리 중단
  Data Error -> 해당 주기 처리 중단
  System Error (data Error) -> mission 해당 주기 처리 중단 ->unity에 전달 
               (fuel low) -> mission 해당 주기 처리(2차 구현시:긴급임무 구현) 

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
  - Medium 후보

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
  - Data Status 및 Communication Status 확인
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
  - current_waypoint 정보
  - mission_state
  - data_status
  - fuel_status

- Monitor Task
  - Fuel Status 및 시스템 상태 정보를 갱신한다.

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

- Monitor Task → Comm TX Task
  - 최신 시스템 상태 정보를 전달한다.
  - 전달 대상에는 Data Status, Fuel Status 등이 포함된다.
  - 각 상태 정보는 가장 최근의 상태값 유지가 중요하므로 최신값 우선 정책을 적용한다.
  - 새로운 상태가 생성되면 기존 상태값을 갱신한다.
  - Comm TX Task는 가장 최근의 상태 정보를 읽어 Output Data에 반영한다.
  - 데이터를 읽어도 상태값이 유지되는 비소비 방식(Peek)을 적용한다.
  - 각 상태의 초기값은 UNKNOWN 또는 초기 상태값으로 설정한다.

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
  - 1차 구현에서는 Mission Task의 정상 임무 흐름을 유지하고 Fuel Status만 Unity Simulator로 송신한다.
  - 2차 구현에서는 Fuel Low 상황에 대응하는 긴급 임무 로직을 수행한다.
  - Comm TX Task는 최신 Output Data와 Fuel Status를 Unity Simulator로 송신한다.
