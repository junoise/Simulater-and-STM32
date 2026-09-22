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
    public bool dtrEnable = false;

    [Header("Test Panel")]
    public bool showPanel = true;

    [Header("Runtime")]
    [SerializeField] private string status = "DISCONNECTED";
    [SerializeField] private int missionNumber;
    [SerializeField] private int acceptedWaypointPackets;
    [SerializeField] private int acceptedOutputPackets;
    [SerializeField] private int rejectedPayloads;
    [SerializeField] private int stalePackets;
    [SerializeField] private int ignoredPackets;

    private enum MissionPhase
    {
        Idle,
        WaitingRoute,
        WaitingNavigate,
        Active,
        Complete,
        Failed
    }

    private MissionPhase phase = MissionPhase.Idle;

    private MissionInterface interfaceLink;
    private MockMissionComputer mock;

    private Session session;
    private bool subscribed;
    private bool faultHandled;

    private string[] ports = Array.Empty<string>();

    private UnityMissionRequest requestedDestination;
    private double requestStarted;
    private double lastValidOutput = double.NegativeInfinity;
    private byte lastWaypointIndex;

    private const double CurrentIntervalSeconds = 0.05;
    private const double SnapshotMaximumAge = 0.2;
    private const double ReceiveMaximumAge = 0.5;
    private const double MissionSetupTimeout = 8.0;

    private const double MinimumNewDestinationDistance = 100.0;
    private const double EndpointDistanceTolerance = 10.0;
    private const float EndpointAltitudeTolerance = 1f;

    private sealed class StartJob
    {
        public byte[] Frame;
        public int Generation;
    }

    private sealed class Received
    {
        public MissionSerialProtocol.Packet Packet;
        public double Time;

        public int Generation;
    }

    private sealed class Session
    {
        public readonly object Gate = new object();

        public readonly ConcurrentQueue<Received> Incoming =
            new ConcurrentQueue<Received>();

        public Thread Thread;

        public volatile bool Stop;
        public volatile bool Connected;
        public volatile string Failure;
        public volatile int SentGeneration;

        public byte[] LatestCurrent;
        public double LatestCurrentTime;
        public StartJob PendingStart;

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

            if (ports.Length > 0 && Array.IndexOf(ports, portName) < 0)
                portName = ports[0];

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
            status = "Already connected or closing.";
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

        missionNumber = 0;
        acceptedWaypointPackets = 0;
        acceptedOutputPackets = 0;
        rejectedPayloads = 0;
        stalePackets = 0;
        ignoredPackets = 0;

        phase = MissionPhase.Idle;
        requestedDestination = default;
        lastValidOutput = double.NegativeInfinity;
        faultHandled = false;

        Session created = new Session();
        session = created;

        interfaceLink.ExternalPeerReady = () => IsConnected;
        interfaceLink.ExternalMissionStartCheck = CheckMissionStart;

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
            interfaceLink.ExternalMissionStartCheck = null;
        }

        if (closing?.Thread != null && closing.Thread.IsAlive)
            closing.Thread.Join(500);

        phase = MissionPhase.Idle;
        status = WorkerAlive ? "CLOSING..." : "DISCONNECTED";
    }

    private string CheckMissionStart(UnityMissionRequest request)
    {
        if (!IsConnected || !subscribed)
            return "Connect the board first.";

        if (Time.timeScale <= 0f)
            return "Resume the simulation before starting a mission.";

        if (phase != MissionPhase.Idle &&
            phase != MissionPhase.Complete)
        {
            return "Wait for MISSION COMPLETE before a new mission.";
        }

        if (phase == MissionPhase.Complete)
        {
            if (Now - lastValidOutput > 1.0)
                return "Waiting for a fresh COMPLETE response.";

            double separation = MissionInterfaceRules.DistanceMeters(
                requestedDestination.destination_latitude,
                requestedDestination.destination_longitude,
                request.destination_latitude,
                request.destination_longitude);

            if (separation < MinimumNewDestinationDistance)
            {
                return "Choose a new destination at least 100 m away. " +
                       "Use USE AHEAD.";
            }
        }

        return "";
    }

    private void OnMissionStart(UnityMissionRequest request)
    {
        Session current = session;

        if (!IsConnected || current == null)
        {
            interfaceLink.ClearMission("Board is not connected.");
            return;
        }

        missionNumber++;
        requestedDestination = request;

        phase = MissionPhase.WaitingRoute;
        requestStarted = Now;
        lastValidOutput = double.NegativeInfinity;
        lastWaypointIndex = 0;

        acceptedWaypointPackets = 0;
        acceptedOutputPackets = 0;

        lock (current.Gate)
        {
            current.PendingStart = new StartJob
            {
                Frame = MissionSerialProtocol.EncodeDestination(request),
                Generation = missionNumber
            };

            current.LatestCurrent =
                MissionSerialProtocol.EncodeCurrent(
                    interfaceLink.CurrentState);

            current.LatestCurrentTime = Now;
        }

        status = $"MISSION {missionNumber}: waiting for new route.";
    }

    private bool RouteMatchesRequest(MissionWaypoint[] route)
    {
        if (route == null || route.Length != 5)
            return false;

        MissionWaypoint last = route[4];

        double distance = MissionInterfaceRules.DistanceMeters(
            last.waypoint_latitude,
            last.waypoint_longitude,
            requestedDestination.destination_latitude,
            requestedDestination.destination_longitude);

        float altitudeDifference = Mathf.Abs(
            last.waypoint_altitude -
            requestedDestination.destination_altitude);

        return distance <= EndpointDistanceTolerance &&
               altitudeDifference <= EndpointAltitudeTolerance;
    }

    private void FailConnection(string reason)
    {
        Disconnect();
        phase = MissionPhase.Failed;
        status = reason;

        interfaceLink.ClearMission(reason);
        Debug.LogWarning("Stm32SerialLink: " + reason, this);
    }

    private void HandlePacket(Received received)
    {
        var packet = received.Packet;

        if (packet.Type == MissionSerialProtocol.WaypointType)
        {
            if (!MissionSerialProtocol.TryDecodeWaypoints(
                    packet.Payload,
                    out MissionWaypoint[] route))
            {
                rejectedPayloads++;
                return;
            }

            if (phase != MissionPhase.WaitingRoute)
            {
                ignoredPackets++;
                return;
            }

            if (!RouteMatchesRequest(route))
            {
                rejectedPayloads++;
                status = "Ignored route: final destination does not match.";
                return;
            }

            interfaceLink.ReceiveWaypointList(route);
            acceptedWaypointPackets++;

            phase = MissionPhase.WaitingNavigate;
            status = "New route received. Waiting for NAVIGATE / VALID.";
            return;
        }

        if (packet.Type != MissionSerialProtocol.OutputType)
            return;

        if (!MissionSerialProtocol.TryDecodeOutput(
                packet.Payload,
                out MissionCommand command))
        {
            rejectedPayloads++;
            return;
        }

        bool valid =
            command.data_status == (byte)DataStatusCode.VALID;

        bool navigate =
            command.mission_state == (byte)MissionStateCode.NAVIGATE;

        bool complete =
            command.mission_state == (byte)MissionStateCode.MISSION_COMPLETE;

        if (phase == MissionPhase.WaitingRoute)
        {
        
            ignoredPackets++;
            return;
        }

        if (phase == MissionPhase.WaitingNavigate)
        {
            if (!navigate || !valid || command.current_waypoint_index != 0)
            {
                ignoredPackets++;
                return;
            }

            interfaceLink.ReceiveCommand(command);

            if (!interfaceLink.TryGetGuidance(out _, out _))
                return;

            phase = MissionPhase.Active;
            lastWaypointIndex = 0;
            lastValidOutput = Now;
            acceptedOutputPackets++;

            status = $"MISSION {missionNumber}: READY. AP ON is available.";
            return;
        }

        if (phase != MissionPhase.Active &&
            phase != MissionPhase.Complete)
        {
            ignoredPackets++;
            return;
        }

        if (command.mission_state == (byte)MissionStateCode.INITIALIZE)
        {
            FailConnection(
                "Unexpected INITIALIZE. Check/reset board before reconnecting.");
            return;
        }

        if (phase == MissionPhase.Complete)
        {
            if (!complete || command.current_waypoint_index != 4)
            {
                ignoredPackets++;
                return;
            }

            interfaceLink.ReceiveCommand(command);
            acceptedOutputPackets++;

            if (valid)
                lastValidOutput = Now;

            return;
        }

        if (navigate)
        {
            if (command.current_waypoint_index < lastWaypointIndex)
            {
                ignoredPackets++;
                return;
            }

            interfaceLink.ReceiveCommand(command);
            acceptedOutputPackets++;

            if (valid)
            {
                lastWaypointIndex = command.current_waypoint_index;
                lastValidOutput = Now;
            }

            return;
        }

        if (complete && command.current_waypoint_index == 4)
        {
            interfaceLink.ReceiveCommand(command);
            acceptedOutputPackets++;

            if (valid && interfaceLink.TryGetGuidance(out _, out _))
            {
                phase = MissionPhase.Complete;
                lastValidOutput = Now;

                status =
                    $"MISSION {missionNumber}: COMPLETE. " +
                    "AP OFF -> USE AHEAD -> MISSION START.";
            }

            return;
        }

        ignoredPackets++;
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
            FailConnection("SERIAL ERROR: " + failure);
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
            status = "PORT OPEN: ready for first mission.";
        }

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

            if (missionNumber == 0 ||
                received.Generation != missionNumber ||
                current.SentGeneration != missionNumber ||
                !interfaceLink.MissionRequested)
            {
                ignoredPackets++;
                continue;
            }

            HandlePacket(received);

            if (!IsConnected)
                return;
        }

        if ((phase == MissionPhase.WaitingRoute ||
             phase == MissionPhase.WaitingNavigate) &&
            Now - requestStarted > MissionSetupTimeout)
        {
            FailConnection(
                "Mission setup timeout. No automatic retry. " +
                "Check/reset board before reconnecting.");
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

                var parser = new MissionSerialProtocol.Parser();

                byte[] readBuffer = new byte[4096];
                double nextCurrentTime = Now;
                int receiveGeneration = 0;

                while (!current.Stop)
                {
                    double now = Now;

                    byte[] statePacket;
                    double stateTime;
                    StartJob job;

                    lock (current.Gate)
                    {
                        statePacket = current.LatestCurrent;
                        stateTime = current.LatestCurrentTime;

                        bool stateFresh =
                            statePacket != null &&
                            now - stateTime <= SnapshotMaximumAge;

                        job = stateFresh ? current.PendingStart : null;

                        if (job != null)
                            current.PendingStart = null;
                    }

                    if (job != null)
                    {
                  
                        port.DiscardInBuffer();
                        parser.Clear();

                        while (current.Incoming.TryDequeue(out _))
                        {
                        }

                        port.Write(job.Frame, 0, job.Frame.Length);
                        Interlocked.Increment(ref current.TxDestination);

                        port.Write(statePacket, 0, statePacket.Length);
                        Interlocked.Increment(ref current.TxCurrent);

                        receiveGeneration = job.Generation;
                        current.SentGeneration = job.Generation;

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
                                Time = receivedAt,
                                Generation = receiveGeneration
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

            Debug.Log("Protocol self-test PASS.", this);
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

        float width = Mathf.Min(540f, Screen.width - 20f);

        GUILayout.BeginArea(
            new Rect((Screen.width - width) * 0.5f, 10f, width, 330f),
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

        GUILayout.Label(MockActive ? "MODE: LOCAL MOCK" : "MODE: BOARD");
        GUILayout.Label("COM: " + (IsConnected ? "OPEN" : "CLOSED"));

        GUILayout.Label(
            $"Mission attempt: {missionNumber} / Phase: {phase}");

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
                $"This mission WP: {acceptedWaypointPackets} / " +
                $"Output: {acceptedOutputPackets}");

            GUILayout.Label(
                $"Frame errors: {current.ParserErrors} / " +
                $"Payload errors: {rejectedPayloads}");

            GUILayout.Label(
                $"Stale: {stalePackets} / Ignored: {ignoredPackets}");
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