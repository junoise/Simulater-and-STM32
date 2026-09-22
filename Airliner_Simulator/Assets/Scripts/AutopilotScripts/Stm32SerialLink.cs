using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

[RequireComponent(typeof(MissionInterface))]
public class Stm32SerialLink : MonoBehaviour
{
    [Header("USB Serial")]
    public string portName = "COM3";
    public bool connectOnStart = false;

    [Tooltip("보드의 USB 시리얼 구현에서 요구할 때만 활성화")]
    public bool dtrEnable = false;

    [Header("Test Panel")]
    public bool showPanel = true;

    [Header("Runtime")]
    [SerializeField] private string status = "DISCONNECTED";
    [SerializeField] private int acceptedWaypointPackets;
    [SerializeField] private int acceptedOutputPackets;
    [SerializeField] private int rejectedPayloads;
    [SerializeField] private int stalePackets;

    private MissionInterface interfaceLink;
    private MockMissionComputer mock;

    private Session session;
    private bool subscribed;
    private bool faultHandled;

    private string[] ports = Array.Empty<string>();

    private const double CurrentIntervalSeconds = 0.05;
    private const double SnapshotMaximumAge = 0.2;
    private const double ReceiveMaximumAge = 0.5;

    private sealed class Received
    {
        public MissionSerialProtocol.Packet Packet;
        public double Time;
    }

    private sealed class Session
    {
        public readonly object Gate = new object();

        public readonly ConcurrentQueue<Received> Incoming =
            new ConcurrentQueue<Received>();

        public Thread Thread;

        public volatile bool Stop;
        public volatile bool Connected;
        public volatile bool MissionSent;
        public volatile string Failure;

        public byte[] LatestCurrent;
        public double LatestCurrentTime;
        public byte[] PendingDestination;

        public bool MissionSubmitted;

        public int TxCurrent;
        public int TxDestination;
        public int RxPackets;
        public int RxBytes;
        public int ParserErrors;
    }

    private static double Now =>
        (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    private bool MockActive =>
        mock != null && mock.isActiveAndEnabled;

    public bool IsConnected =>
        session != null &&
        session.Connected &&
        !session.Stop &&
        string.IsNullOrEmpty(session.Failure) &&
        !MockActive;

    private bool WorkerAlive =>
        session != null &&
        session.Thread != null &&
        session.Thread.IsAlive;

    private void Awake()
    {
        interfaceLink = GetComponent<MissionInterface>();
        mock = GetComponent<MockMissionComputer>();
    }

    private void Start()
    {
        RefreshPorts();

        if (connectOnStart)
            Connect();
    }

    public void RefreshPorts()
    {
        try
        {
            ports = SerialPort.GetPortNames();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);

            if (ports.Length > 0 &&
                Array.IndexOf(ports, portName) < 0)
            {
                portName = ports[0];
            }

            if (ports.Length == 0)
                status = "No COM ports found.";
        }
        catch (Exception exception)
        {
            ports = Array.Empty<string>();
            status = "Port scan failed: " + exception.Message;
        }
    }

    public void Connect()
    {
        if (!Application.isPlaying || !isActiveAndEnabled)
            return;

        if (WorkerAlive)
        {
            status = "Already connected or closing. Wait briefly.";
            return;
        }

        if (MockActive)
        {
            status = "Disable MockMissionComputer before connecting.";
            return;
        }

        if (string.IsNullOrWhiteSpace(portName))
        {
            status = "Select a COM port.";
            return;
        }

        interfaceLink.ClearMission();

        acceptedWaypointPackets = 0;
        acceptedOutputPackets = 0;
        rejectedPayloads = 0;
        stalePackets = 0;

        faultHandled = false;

        Session created = new Session();
        session = created;

        interfaceLink.ExternalPeerReady = () => IsConnected;

        string selectedPort = portName.Trim();
        bool selectedDtr = dtrEnable;

        created.Thread = new Thread(
            () => RunWorker(created, selectedPort, selectedDtr))
        {
            IsBackground = true,
            Name = "STM32 Serial"
        };

        status = "CONNECTING: " + selectedPort;
        created.Thread.Start();
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        interfaceLink.MissionStartProduced += OnMissionStart;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || interfaceLink == null)
            return;

        interfaceLink.MissionStartProduced -= OnMissionStart;
        subscribed = false;
    }

    public void Disconnect()
    {
        Session closing = session;

        if (closing != null)
            closing.Stop = true;

        Unsubscribe();

        if (interfaceLink != null && closing != null)
        {
            interfaceLink.ClearMission("Board disconnected.");
            interfaceLink.ExternalPeerReady = null;
        }

        // 포트 자체는 통신 스레드에서 닫음
        if (closing?.Thread != null && closing.Thread.IsAlive)
            closing.Thread.Join(500);

        status = WorkerAlive ? "CLOSING..." : "DISCONNECTED";
    }

    private void OnMissionStart(UnityMissionRequest request)
    {
        Session current = session;

        if (!IsConnected || current == null)
        {
            interfaceLink.ClearMission("Board is not connected.");
            return;
        }

        bool duplicate;

        lock (current.Gate)
        {
            duplicate = current.MissionSubmitted;

            if (!duplicate)
            {
                current.MissionSubmitted = true;

                current.PendingDestination =
                    MissionSerialProtocol.EncodeDestination(request);

                current.LatestCurrent =
                    MissionSerialProtocol.EncodeCurrent(
                        interfaceLink.CurrentState);

                current.LatestCurrentTime = Now;
            }
        }

        if (duplicate)
        {
            // 현재 프로토콜에는 mission ID와 시작 명령 ACK가 없음
            // 초기 연결 시험에서는 한 연결당 한 임무만 허용
            Disconnect();

            status = "Reset board and reconnect before a new mission.";

            interfaceLink.ClearMission(
                "Reset board and reconnect before a new mission.");
        }
        else
        {
            status = "MISSION QUEUED: waiting for board response.";
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
            showPanel = !showPanel;

        Session current = session;

        if (current == null)
            return;

        if (!string.IsNullOrEmpty(current.Failure) && !faultHandled)
        {
            string failure = current.Failure;
            faultHandled = true;

            Disconnect();

            status = "SERIAL ERROR: " + failure;
            Debug.LogError("Stm32SerialLink: " + failure, this);
            return;
        }

        if (MockActive && WorkerAlive)
        {
            Disconnect();
            status = "Disconnected: MockMissionComputer is enabled.";
            return;
        }

        if (!IsConnected)
            return;

        if (!subscribed)
        {
            Subscribe();
            status = "PORT OPEN: waiting for STM32 packets.";
        }

        // Unity API는 메인 스레드에서만 읽음
        // 통신 스레드는 이 상태의 바이너리 복사본만 사용
        if (interfaceLink.HasAircraftState && Time.timeScale > 0f)
        {
            byte[] snapshot =
                MissionSerialProtocol.EncodeCurrent(
                    interfaceLink.CurrentState);

            lock (current.Gate)
            {
                current.LatestCurrent = snapshot;
                current.LatestCurrentTime = Now;
            }
        }
        else
        {
            lock (current.Gate)
                current.LatestCurrent = null;
        }

        int processed = 0;

        while (processed++ < 128 &&
               current.Incoming.TryDequeue(out Received received))
        {
            if (Now - received.Time > ReceiveMaximumAge)
            {
                stalePackets++;
                continue;
            }

            if (!current.MissionSent || !interfaceLink.MissionRequested)
                continue;

            var packet = received.Packet;

            if (packet.Type == MissionSerialProtocol.WaypointType)
            {
                if (MissionSerialProtocol.TryDecodeWaypoints(
                        packet.Payload,
                        out MissionWaypoint[] route))
                {
                    interfaceLink.ReceiveWaypointList(route);
                    acceptedWaypointPackets++;
                    status = "Received 5 waypoints.";
                }
                else
                {
                    rejectedPayloads++;
                    status = "Rejected waypoint payload.";
                }
            }
            else if (packet.Type == MissionSerialProtocol.OutputType)
            {
                if (MissionSerialProtocol.TryDecodeOutput(
                        packet.Payload,
                        out MissionCommand command))
                {
                    interfaceLink.ReceiveCommand(command);
                    acceptedOutputPackets++;
                }
                else
                {
                    rejectedPayloads++;
                    status = "Rejected output payload.";
                }
            }
        }
    }

    private static void RunWorker(
        Session current,
        string selectedPort,
        bool selectedDtr)
    {
        try
        {
            using (SerialPort port = new SerialPort(
                       selectedPort, 115200, Parity.None, 8, StopBits.One))
            {
                port.Handshake = Handshake.None;
                port.DtrEnable = selectedDtr;
                port.RtsEnable = false;

                port.ReadTimeout = 20;
                port.WriteTimeout = 100;

                port.ReadBufferSize = 8192;
                port.WriteBufferSize = 4096;

                port.Open();
                port.DiscardInBuffer();

                current.Connected = true;

                MissionSerialProtocol.Parser parser =
                    new MissionSerialProtocol.Parser();

                byte[] readBuffer = new byte[4096];
                double nextCurrentTime = Now;

                while (!current.Stop)
                {
                    double now = Now;

                    byte[] statePacket;
                    byte[] destinationPacket;
                    double stateTime;

                    lock (current.Gate)
                    {
                        statePacket = current.LatestCurrent;
                        stateTime = current.LatestCurrentTime;

                        // 유효한 현재 상태가 있을 때 목적지 다음에
                        // Current 패킷을 바로 보냄
                        bool stateFresh =
                            statePacket != null &&
                            now - stateTime <= SnapshotMaximumAge;

                        destinationPacket = stateFresh
                            ? current.PendingDestination
                            : null;

                        if (destinationPacket != null)
                            current.PendingDestination = null;
                    }

                    if (destinationPacket != null)
                    {
                        // 임무 시작 전 도착한 응답은 제거
                        port.DiscardInBuffer();
                        parser.Clear();

                        while (current.Incoming.TryDequeue(out _))
                        {
                        }

                        port.Write(
                            destinationPacket, 0, destinationPacket.Length);

                        Interlocked.Increment(ref current.TxDestination);

                        port.Write(statePacket, 0, statePacket.Length);
                        Interlocked.Increment(ref current.TxCurrent);

                        current.MissionSent = true;
                        nextCurrentTime = Now + CurrentIntervalSeconds;
                    }
                    else if (now >= nextCurrentTime)
                    {
                        nextCurrentTime = now + CurrentIntervalSeconds;

                        if (statePacket != null &&
                            now - stateTime <= SnapshotMaximumAge)
                        {
                            port.Write(statePacket, 0, statePacket.Length);
                            Interlocked.Increment(ref current.TxCurrent);
                        }
                    }

                    int available = port.BytesToRead;

                    if (available > 0)
                    {
                        int count = port.Read(
                            readBuffer, 0,
                            Math.Min(available, readBuffer.Length));

                        Interlocked.Add(ref current.RxBytes, count);

                        double receivedAt = Now;
                        parser.Feed(readBuffer, count, receivedAt);

                        while (parser.TryRead(
                                   receivedAt,
                                   out MissionSerialProtocol.Packet packet))
                        {
                            Interlocked.Increment(ref current.RxPackets);

                            if (current.Incoming.Count >= 128)
                                throw new IOException("RX queue overflow.");

                            current.Incoming.Enqueue(new Received
                            {
                                Packet = packet,
                                Time = receivedAt
                            });
                        }
                    }
                    else
                    {
                        parser.Expire(Now);
                    }

                    current.ParserErrors = parser.Errors;

                    Thread.Sleep(2);
                }
            }
        }
        catch (Exception exception)
        {
            if (!current.Stop)
            {
                current.Failure =
                    exception.GetType().Name + ": " + exception.Message;
            }
        }
        finally
        {
            current.Connected = false;
        }
    }

    [ContextMenu("Run Protocol Self Test")]
    public void RunProtocolSelfTest()
    {
        try
        {
            MissionSerialProtocol.RunSelfTest();
            status = "PROTOCOL SELF TEST: PASS";

            Debug.Log(
                "Protocol self-test PASS: CRC, encoding, decoding, " +
                "fragmentation, joined packets, CRC recovery and timeout.",
                this);
        }
        catch (Exception exception)
        {
            status = "PROTOCOL SELF TEST: FAIL";
            Debug.LogException(exception, this);
        }
    }

    private void OnGUI()
    {
        if (!showPanel)
            return;

        float width = Mathf.Min(500f, Screen.width - 20f);

        GUILayout.BeginArea(
            new Rect((Screen.width - width) * 0.5f, 10f, width, 275f),
            GUI.skin.box);

        GUILayout.Label("STM32 USB SERIAL / F8: SHOW OR HIDE");
        GUILayout.Label("115200 / 8-N-1 / SPEED: m/s");

        GUILayout.BeginHorizontal();

        bool oldEnabled = GUI.enabled;
        GUI.enabled = !WorkerAlive;

        if (GUILayout.Button("REFRESH PORTS"))
            RefreshPorts();

        if (GUILayout.Button("NEXT PORT") && ports.Length > 0)
        {
            int index = Array.IndexOf(ports, portName);
            portName = ports[(index + 1 + ports.Length) % ports.Length];
        }

        GUI.enabled = oldEnabled;

        GUILayout.Label(portName);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("CONNECT"))
            Connect();

        if (GUILayout.Button("DISCONNECT"))
            Disconnect();

        if (GUILayout.Button("SELF TEST"))
            RunProtocolSelfTest();

        GUILayout.EndHorizontal();

        GUILayout.Label(MockActive
            ? "MODE: LOCAL MOCK"
            : "MODE: BOARD");

        GUILayout.Label("COM: " + (IsConnected ? "OPEN" : "CLOSED"));

        Session current = session;

        if (current != null)
        {
            GUILayout.Label(
                $"TX Current: {current.TxCurrent} / " +
                $"Destination: {current.TxDestination}");

            GUILayout.Label(
                $"RX bytes: {current.RxBytes} / " +
                $"CRC-valid packets: {current.RxPackets}");

            GUILayout.Label(
                $"WP: {acceptedWaypointPackets} / " +
                $"Output: {acceptedOutputPackets}");

            GUILayout.Label(
                $"Frame errors: {current.ParserErrors} / " +
                $"Payload errors: {rejectedPayloads} / " +
                $"Stale: {stalePackets}");
        }

        GUILayout.Label(status);

        GUILayout.EndArea();
    }

    private void OnDisable()
    {
        Disconnect();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}