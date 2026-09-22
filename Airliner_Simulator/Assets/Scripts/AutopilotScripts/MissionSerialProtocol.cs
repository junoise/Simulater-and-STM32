using System;
using System.Collections.Generic;
using System.Text;

public static class MissionSerialProtocol
{
    public const byte DestinationType = 0x01;
    public const byte CurrentType = 0x02;
    public const byte WaypointType = 0x03;
    public const byte OutputType = 0x04;

    public sealed class Packet
    {
        public byte Type;
        public byte[] Payload;
    }

    public static ushort Crc16(byte[] bytes, int offset, int count)
    {
        ushort crc = 0xFFFF;

        for (int index = offset; index < offset + count; index++)
        {
            crc ^= (ushort)(bytes[index] << 8);

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (ushort)(
                    (crc & 0x8000) != 0
                        ? (crc << 1) ^ 0x1021
                        : crc << 1
                );
            }
        }

        return crc;
    }

    public static byte[] CreateFrame(byte type, byte[] payload)
    {
        if (payload == null || payload.Length > ushort.MaxValue)
            throw new ArgumentException("Invalid payload.");

        byte[] frame = new byte[payload.Length + 7];

        frame[0] = 0xAA;
        frame[1] = 0x55;
        frame[2] = type;
        frame[3] = (byte)(payload.Length & 0xFF);
        frame[4] = (byte)(payload.Length >> 8);

        Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);

        ushort crc = Crc16(frame, 2, payload.Length + 3);

        frame[5 + payload.Length] = (byte)(crc & 0xFF);
        frame[6 + payload.Length] = (byte)(crc >> 8);

        return frame;
    }

    private static void WriteFloat(byte[] bytes, int offset, float value)
    {
        byte[] encoded = BitConverter.GetBytes(value);

        if (!BitConverter.IsLittleEndian)
            Array.Reverse(encoded);

        Buffer.BlockCopy(encoded, 0, bytes, offset, 4);
    }

    private static float ReadFloat(byte[] bytes, int offset)
    {
        byte[] encoded = new byte[4];
        Buffer.BlockCopy(bytes, offset, encoded, 0, 4);

        if (!BitConverter.IsLittleEndian)
            Array.Reverse(encoded);

        return BitConverter.ToSingle(encoded, 0);
    }

    public static byte[] EncodeDestination(UnityMissionRequest request)
    {
        byte[] payload = new byte[13];

        WriteFloat(payload, 0, request.destination_latitude);
        WriteFloat(payload, 4, request.destination_longitude);
        WriteFloat(payload, 8, request.destination_altitude);

        payload[12] = request.mission_start_command;

        return CreateFrame(DestinationType, payload);
    }

    public static byte[] EncodeCurrent(UnityAircraftState state)
    {
        byte[] payload = new byte[24];

        WriteFloat(payload, 0, state.current_latitude);
        WriteFloat(payload, 4, state.current_longitude);
        WriteFloat(payload, 8, state.current_altitude);
        WriteFloat(payload, 12, state.current_heading);
        WriteFloat(payload, 16, state.current_speed);
        WriteFloat(payload, 20, state.current_fuel);

        return CreateFrame(CurrentType, payload);
    }

    public static bool TryDecodeOutput(
        byte[] payload,
        out MissionCommand command)
    {
        command = default;

        if (payload == null || payload.Length != 15)
            return false;

        command = new MissionCommand
        {
            target_altitude = ReadFloat(payload, 0),
            target_heading = ReadFloat(payload, 4),
            target_speed = ReadFloat(payload, 8),
            current_waypoint_index = payload[12],
            mission_state = payload[13],
            data_status = payload[14]
        };

        return command.current_waypoint_index < 5 &&
               MissionInterfaceRules.CommandValid(command);
    }

    public static bool TryDecodeWaypoints(
        byte[] payload,
        out MissionWaypoint[] waypoints)
    {
        waypoints = null;

        if (payload == null ||
            payload.Length != 61 ||
            payload[0] != 5)
        {
            return false;
        }

        MissionWaypoint[] result = new MissionWaypoint[5];

        for (int index = 0; index < result.Length; index++)
        {
            int offset = 1 + index * 12;

            result[index] = new MissionWaypoint
            {
                waypoint_latitude = ReadFloat(payload, offset),
                waypoint_longitude = ReadFloat(payload, offset + 4),
                waypoint_altitude = ReadFloat(payload, offset + 8)
            };

            if (!MissionInterfaceRules.WaypointValid(result[index]))
                return false;
        }

        waypoints = result;
        return true;
    }

    public sealed class Parser
    {
        private readonly List<byte> buffer = new List<byte>();
        private double partialStarted = -1;

        public int Errors { get; private set; }

        public void Clear()
        {
            buffer.Clear();
            partialStarted = -1;
        }

        public void Expire(double now)
        {
            if (partialStarted >= 0 &&
                now - partialStarted >= 0.1)
            {
                Clear();
                Errors++;
            }
        }

        public void Feed(byte[] bytes, int count, double now)
        {
            Expire(now);

            if (count < 0 || count > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (buffer.Count + count > 8192)
            {
                Clear();
                Errors++;
            }

            if (count > 8192)
                return;

            for (int index = 0; index < count; index++)
                buffer.Add(bytes[index]);

            if (buffer.Count > 0 && partialStarted < 0)
                partialStarted = now;
        }

        public bool TryRead(double now, out Packet packet)
        {
            packet = null;
            Expire(now);

            while (buffer.Count >= 2)
            {
                if (buffer[0] != 0xAA || buffer[1] != 0x55)
                {
                    buffer.RemoveAt(0);
                    partialStarted = buffer.Count > 0 ? now : -1;
                    continue;
                }

                if (partialStarted < 0)
                    partialStarted = now;

                if (buffer.Count < 5)
                    return false;

                byte type = buffer[2];
                int length = buffer[3] | (buffer[4] << 8);

                bool allowed =
                    (type == WaypointType && length == 61) ||
                    (type == OutputType && length == 15);

                if (!allowed)
                {
                    buffer.RemoveAt(0);
                    partialStarted = buffer.Count > 0 ? now : -1;
                    Errors++;
                    continue;
                }

                int totalLength = length + 7;

                if (buffer.Count < totalLength)
                    return false;

                byte[] frame = buffer.GetRange(0, totalLength).ToArray();

                ushort receivedCrc = (ushort)(
                    frame[5 + length] |
                    (frame[6 + length] << 8)
                );

                ushort calculatedCrc = Crc16(frame, 2, length + 3);

                if (receivedCrc != calculatedCrc)
                {
                    buffer.RemoveAt(0);
                    partialStarted = buffer.Count > 0 ? now : -1;
                    Errors++;
                    continue;
                }

                byte[] payload = new byte[length];
                Buffer.BlockCopy(frame, 5, payload, 0, length);

                buffer.RemoveRange(0, totalLength);
                partialStarted = buffer.Count > 0 ? now : -1;

                packet = new Packet
                {
                    Type = type,
                    Payload = payload
                };

                return true;
            }

            return false;
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("Self-test failed: " + name);
    }

    public static void RunSelfTest()
    {
        byte[] check = Encoding.ASCII.GetBytes("123456789");

        Require(
            Crc16(check, 0, check.Length) == 0x29B1,
            "CRC check value"
        );

        byte[] destination = EncodeDestination(new UnityMissionRequest
        {
            destination_latitude = 35f,
            destination_longitude = 129f,
            destination_altitude = 1000f,
            mission_start_command = 1
        });

        Require(
            BitConverter.ToString(destination) ==
            "AA-55-01-0D-00-00-00-0C-42-00-00-01-43-00-00-7A-44-01-0D-5D",
            "destination frame"
        );

        byte[] current = EncodeCurrent(new UnityAircraftState
        {
            current_latitude = 35f,
            current_longitude = 129f,
            current_altitude = 1000f,
            current_heading = 0f,
            current_speed = 100f,
            current_fuel = 100f
        });

        Require(current.Length == 31, "current frame length");

        byte[] outputPayload = new byte[15];
        WriteFloat(outputPayload, 0, 1000f);
        WriteFloat(outputPayload, 4, 0f);
        WriteFloat(outputPayload, 8, 100f);
        outputPayload[12] = 0;
        outputPayload[13] = 1;
        outputPayload[14] = 1;

        byte[] output = CreateFrame(OutputType, outputPayload);

        Require(
            BitConverter.ToString(output) ==
            "AA-55-04-0F-00-00-00-7A-44-00-00-00-00-00-00-C8-42-00-01-01-C7-E1",
            "output frame"
        );

        Parser parser = new Parser();
        int packetCount = 0;

        for (int index = 0; index < output.Length; index++)
        {
            parser.Feed(new[] { output[index] }, 1, index * 0.001);

            if (parser.TryRead(index * 0.001, out Packet packet))
            {
                Require(
                    TryDecodeOutput(packet.Payload, out MissionCommand decoded) &&
                    decoded.target_speed == 100f &&
                    decoded.target_altitude == 1000f,
                    "output decode"
                );

                packetCount++;
            }
        }

        Require(packetCount == 1, "fragmented packet");

        parser = new Parser();

        byte[] joined = new byte[output.Length * 2];
        Buffer.BlockCopy(output, 0, joined, 0, output.Length);
        Buffer.BlockCopy(output, 0, joined, output.Length, output.Length);

        parser.Feed(joined, joined.Length, 0);

        Require(parser.TryRead(0, out _), "joined packet 1");
        Require(parser.TryRead(0, out _), "joined packet 2");
        Require(!parser.TryRead(0, out _), "joined packet end");

        parser = new Parser();

        byte[] damaged = (byte[])output.Clone();
        damaged[8] ^= 1;

        parser.Feed(damaged, damaged.Length, 0);
        parser.Feed(output, output.Length, 0);

        Require(parser.TryRead(0, out _), "CRC resynchronization");
        Require(parser.Errors > 0, "CRC rejection");

        parser = new Parser();
        parser.Feed(new[] { output[0], output[1], output[2] }, 3, 0);
        parser.TryRead(0, out _);
        parser.Expire(0.2);

        Require(parser.Errors == 1, "partial packet timeout");

        parser.Feed(output, output.Length, 0.21);
        Require(parser.TryRead(0.21, out _), "timeout recovery");

        byte[] routePayload = new byte[61];
        routePayload[0] = 5;

        for (int index = 0; index < 5; index++)
        {
            int offset = 1 + index * 12;
            WriteFloat(routePayload, offset, 35f + index * 0.01f);
            WriteFloat(routePayload, offset + 4, 129f);
            WriteFloat(routePayload, offset + 8, 1000f);
        }

        Require(
            CreateFrame(WaypointType, routePayload).Length == 68 &&
            TryDecodeWaypoints(routePayload, out MissionWaypoint[] route) &&
            route.Length == 5,
            "five waypoints"
        );

        routePayload[0] = 4;

        Require(
            !TryDecodeWaypoints(routePayload, out _),
            "invalid waypoint count"
        );
    }
}