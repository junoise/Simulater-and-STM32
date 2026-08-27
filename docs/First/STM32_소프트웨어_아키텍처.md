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
                    System Monitor
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
    수신 데이터 범위 및 형식 유효성 검사
    유효한 항공기 상태 데이터 저장
    
  - 출력
    검증된 항공기 상태 데이터
    데이터 유효성 상태
  
  - 내부 상태
    데이터 유효성상태
    현재 항공기 상태 데이터
    
### 3.3 Mission Manager

  - 입력
    System Monitor의 시스템 상태
  
  - 처리
    현재 Mission State를 확인한다.
    시스템 상태가 정상인 경우 현재 Mission State에 따라 필요한 모듈을 호출한다.
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
    현재 목표 waypoint
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

### 3.7 OutputData Manager

## 4. 데이터 흐름

## 5. FreeRTOS Task 아키텍처
