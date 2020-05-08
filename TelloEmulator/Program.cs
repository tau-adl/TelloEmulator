using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace TelloEmulator
{
    struct Vector3D
    {
        public float X;
        public float Y;
        public float Z;


        public Vector3D(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"{X:F2},{Y:F2},{Z:F2}";
        }
    }

    class Program
    {
        private const string TelloPrompt = "tello> ";

        private static bool _motorsOn;
        private static int _height;
        private static int _battery = 67;
        private static int _speed = 100;

        private static int _pitch;
        private static int _roll = 2;
        private static int _yaw;

        private static int _tof;
        private static int _flightTime;

        private static int _mid = -1;
        private static Vector3D _mpry;

        private static float _barometer = 0.795338F;
        private static Vector3D _position;
        private static Vector3D _velocity;
        private static Vector3D _acceleration;

        private static int _tempLow = 58;
        private static int _tempHigh = 62;
        private static int _wifiSnr;

        private static object _flightDataTimeState;
        private static Timer _flightDataTimer;
        private static Timer _telemetryTimer;
        private readonly static Dictionary<IPEndPoint, DateTime> _commandRemoteEndPoint = new Dictionary<IPEndPoint, DateTime>();
        private static ManualResetEvent _readyWaitHandle;


        private static void Reset()
        {
            Land();
            _mid = -1;
            _speed = 100;
            _yaw = 0;
            _pitch = 0;
            _roll = 2;
            _tof = 0;
            _flightTime = 0;
            _barometer = 0.795338F;
            _wifiSnr = 0;
            _acceleration = new Vector3D(-1, 5, -1001);
            _velocity = new Vector3D();
            _position = new Vector3D();
            _mpry = new Vector3D();
            if (_telemetryTimer != null)
            {
                _telemetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _telemetryTimer.Dispose();
                _telemetryTimer = null;
            }
        }

        static string ProcessMoveCommand(string[] words, bool minus, ref int value)
        {
            if (words.Length != 2)
                return $"unknown command: {words[0]}";

            if (!int.TryParse(words[1], out var cm) || cm < 20 || cm > 500)
                return "out of range";

            if (!_motorsOn)
                return "error Motor stop";

            if (minus)
                value -= cm;
            else
                value += cm;

            return "ok";
        }

        static string ProcessRotateCommand(string[] words, bool minus, ref int value)
        {
            if (words.Length != 2)
                return $"unknown command: {words[0]}";

            if (!int.TryParse(words[1], out var degrees) || degrees < 1 || degrees > 3600)
                return "out of range";

            if (!_motorsOn)
                return "error Motor stop";

            if (minus)
            {
                value -= degrees;
                while (value < -180)
                    value += 360;
            }
            else
            {
                value += degrees;
                while (value >= 180)
                    value -= 360;
            }
            return "ok";
        }

        private static void Land()
        {
            _height = 0;
            _motorsOn = false;
            if (_flightDataTimer != null)
            {
                _flightDataTimeState = null;
                _flightDataTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _flightDataTimer.Dispose();
                _flightDataTimer = null;
            }
        }

        private static void FlightDataTimerWork(object state)
        {
            if (state != _flightDataTimeState)
                return;
            ++_flightTime;
        }

        private static string ProcessCommand(string command)
        {
            if (command == null)
                return null;
            var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return "error";
            switch (words[0])
            {
                case "command":
                    if (words.Length != 1)
                        goto default;
                    return "ok";
                case "takeoff":
                    if (words.Length != 1)
                        goto default;
                    _motorsOn = true;
                    _height = 1;
                    Interlocked.MemoryBarrier();
                    _flightDataTimeState = new object();
                    _flightDataTimer = new Timer(FlightDataTimerWork, _flightDataTimeState, 1000, 1000);
                    return "ok";
                case "land":
                    if (words.Length != 1)
                        goto default;
                    if (_motorsOn)
                        Thread.Sleep(1000);
                    Land();
                    return "ok";
                case "battery?":
                    if (words.Length != 1)
                        goto default;
                    return _battery.ToString("D");
                case "wifi?":
                    if (words.Length != 1)
                        goto default;
                    return _wifiSnr.ToString("D");
                case "speed?":
                    if (words.Length != 1)
                        goto default;
                    return $"{_speed}.0";
                case "sn?":
                    if (words.Length != 1)
                        goto default;
                    return "0TQDG7REDBD9ZC";
                case "height?":
                    if (words.Length != 1)
                        goto default;
                    return $"{_height/10}dm";
                case "temp?":
                    if (words.Length != 1)
                        goto default;
                    return $"{_tempLow}~{_tempHigh}C";
                case "baro?":
                    if (words.Length != 1)
                        goto default;
                    return _barometer.ToString("F6");
                case "tof?":
                    if (words.Length != 1)
                        goto default;
                    return $"{_tof*10}mm";
                case "time?":
                    if (words.Length != 1)
                        goto default;
                    return $"{_flightTime}s";
                case "attitude?":
                    if (words.Length != 1)
                        goto default;
                    return $"pitch:{_pitch};roll:{_roll};yaw:{_yaw};";
                case "acceleration?":
                    if (words.Length != 1)
                        goto default;
                    return $"agx:{_acceleration.X:F2};agy:{_acceleration.Y:F2};agz:{_acceleration.Z:F2};";
                case "sdk?":
                    if (words.Length != 1)
                        goto default;
                    return "20";
                case "mon":
                case "moff":
                case "streamon":
                case "streamoff":
                    if (words.Length != 1)
                        goto default;
                    return "ok";
                case "emergency":
                    if (words.Length != 1)
                        goto default;
                    Land();
                    return null;
                case "up":
                    var upResponse = ProcessMoveCommand(words, false, ref _height);
                    if (_height > 500)
                        _height = 500;
                    return upResponse;
                case "down":
                   var downResponse = ProcessMoveCommand(words, true, ref _height);
                   if (_height < 10)
                       _height = 10;
                   return downResponse;
                case "right":
                    return ProcessMoveCommand(words, false, ref _tof);
                case "left":
                    return ProcessMoveCommand(words, true, ref _tof);
                case "forward":
                    return ProcessMoveCommand(words, false, ref _tof);
                case "backward":
                    return ProcessMoveCommand(words, true, ref _tof);
                case "cw":
                    return ProcessRotateCommand(words, false, ref _yaw);
                case "ccw":
                    return ProcessRotateCommand(words, true, ref _yaw);
                case "speed":
                    if (words.Length != 2)
                        goto default;
                    if (!int.TryParse(words[1], out var speed) || speed < 10 || speed > 100)
                        return "out of range";
                    _speed = speed;
                    return "ok";
                case "flip":
                    if (words.Length != 2)
                        goto default;
                    switch (words[1])
                    {
                        case "l":
                        case "r":
                        case "f":
                        case "b":
                            return "ok";
                        default:
                            return "error";
                    }
                case "rc":
                    // the drone expects two or more arguments.
                    // in case more than 4 arguments are provided they are ignored.
                    if (words.Length < 3)
                        goto default;
                    for (int i = 1; i < Math.Max(words.Length, 5); ++i)
                        if (!int.TryParse(words[i], out var value) || value < -100 || value > 100)
                            return "out of range";
                    return null;
                case "wifi":
                    if (words.Length != 3)
                        goto default;
                    Reset();
                    return "ok";
                default:
                    return $"unknown command: {words[0]}";

            }
        }

        private static void ReceiverWork(object state)
        {
            Console.WriteLine("Tello command thread started.");
            var socket = (Socket) state;
            try
            {
                var buffer = new byte[1024];
                _readyWaitHandle.Set();
                while (true)
                {
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    var count = socket.ReceiveFrom(buffer, SocketFlags.None, ref remoteEndPoint);
                    if (count == 0)
                        return;
                    var command = Encoding.ASCII.GetString(buffer, 0, count);
                    var response = ProcessCommand(command);

                    Console.WriteLine($"Remote Command: {command}, Response: {response}");
                    if (response != null)
                    {
                        socket.SendTo(Encoding.ASCII.GetBytes(response), remoteEndPoint);
                        // handle special commands:
                        if (response == "ok")
                        {
                            if (command == "command")
                            {
                                var remoteIPEndPoint = (IPEndPoint)remoteEndPoint;
                                lock (_commandRemoteEndPoint)
                                {
                                    if (!_commandRemoteEndPoint.ContainsKey(remoteIPEndPoint))
                                    {
                                        Console.WriteLine($"INFO: Command channel established with {remoteEndPoint}");
                                        _commandRemoteEndPoint[remoteIPEndPoint] = DateTime.Now;
                                        Interlocked.MemoryBarrier();
                                        Console.WriteLine($"INFO: Starting telemetry transmission to {remoteIPEndPoint.Address} every 10Hz");
                                        if (_telemetryTimer == null)
                                            _telemetryTimer = new Timer(TelemetryTimerWork, socket, 0, 100);
                                        Console.WriteLine(TelloPrompt);
                                    }
                                    else
                                    {
                                        _commandRemoteEndPoint[(IPEndPoint)remoteEndPoint] = DateTime.Now;
                                    }
                                }
                            }
                            else if (command.StartsWith("wifi "))
                                Thread.Sleep(6000);
                        }
                    }
                }
            }
            catch (SocketException sex)
            {
                if (sex.SocketErrorCode != SocketError.Interrupted)
                    Console.WriteLine(sex);
            }
            catch (ThreadAbortException)
            {
                // suppress exception.
            }
            catch (InvalidOperationException)
            {
                // suppress exception.
            }
            Console.WriteLine("Tello command thread stopped.");
        }

        private static void TelemetryTimerWork(object state)
        {
            var socket = (Socket) state;
            try
            {
                KeyValuePair<IPEndPoint, DateTime>[] remoteEndPoints;
                lock (_commandRemoteEndPoint)
                {
                    remoteEndPoints = _commandRemoteEndPoint.ToArray();
                }
                var telemetry = $"mid:{_mid};x:{_position.X:F0};y:{_position.Y:F0};z:{_position.Z:F0};mpry:{_mpry.X:F0},{_mpry.Y:F0},{_mpry.Z:F0};pitch:{_pitch};roll:{_roll};yaw:{_yaw};vgx:{_velocity.X:F0};vgy:{_velocity.Y:F0};vgz:{_velocity.Z:F0};templ:{_tempLow};temph:{_tempHigh};tof:{_tof};h:{_height/10};bat:{_battery};baro:{_barometer:0.00####};time:{_flightTime};agx:{_acceleration.X:F2};agy:{_acceleration.Y:F2};agz:{_acceleration.Z:F2};";
                var bytes = Encoding.ASCII.GetBytes(telemetry);
                var garbage = new List<IPEndPoint>();
                foreach (var pair in remoteEndPoints)
                {
                    var lastCommand = DateTime.Now - pair.Value;
                    if (lastCommand.TotalSeconds > 60)
                    {
                        garbage.Add(pair.Key);
                        continue;
                    }
                    try
                    {
                        socket.SendTo(bytes, pair.Key);
                    }
                    catch (SocketException)
                    {
                        garbage.Add(pair.Key);
                    }
                }
                if (garbage.Count > 0)
                {
                    lock (_commandRemoteEndPoint)
                    {
                        foreach (var key in garbage)
                            _commandRemoteEndPoint.Remove(key);
                    }
                }
            }
            catch (SocketException sex)
            {
                if (sex.SocketErrorCode != SocketError.Interrupted)
                    Console.Error.WriteLine(sex);
            }
            catch (InvalidOperationException)
            {
                // suppress exception.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        static void HandleUserCommand(string[] words, string units, int rangeMin, int rangeMax, ref int field)
        {
            if (words.Length == 1)
            {
                Console.WriteLine($"Field '{words[0]}' value is {field}{units}.");
            }
            else if (words.Length == 2 && int.TryParse(words[1], out var value) &&
                value >= rangeMin && value <= rangeMax)
            {
                field = value;
                Console.WriteLine($"Field '{words[0]}' set to {value}{units}");
            }
            else
            {
                Console.WriteLine($"Usage: {words[0]} [value]\n" +
                                  $"       where 'value' must be an integer between {rangeMin} and {rangeMax}.");
            }
        }

        private static void Main(string[] args)
        {
            Console.WriteLine("DJI Tello Drone Emulator v1.0\n");
            var localEndPoint = new IPEndPoint(IPAddress.Any, 8889);
            Console.Write($"Creating a UDP server on {localEndPoint}... ");
            try
            {
                // initialize fields:
                _readyWaitHandle = new ManualResetEvent(false);
                Reset();
                // create a UDP server socket:
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(localEndPoint);
                Console.WriteLine("Success");
                // start the receiver thread:
                Console.WriteLine("Starting Tello command thread...");
                var receiver = new Thread(ReceiverWork) {IsBackground = true};
                Interlocked.MemoryBarrier();
                receiver.Start(socket);
                if (!_readyWaitHandle.WaitOne(3000))
                {
                    Console.WriteLine("FAILED");
                    Console.Error.WriteLine("ERROR: Timeout while waiting for the Tello command thread to start.");
                    return;
                }

                var quit = false;
                Console.WriteLine();
                while (!quit)
                {
                    Console.Write(TelloPrompt);
                    var line = Console.ReadLine();
                    if (line == null)
                        break;
                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0)
                        continue;
                    switch (words[0].ToLower())
                    {
                        case "exit":
                        case "quit":
                            quit = true;
                            break;
                        case "reset":
                            Reset();
                            break;
                        case "battery":
                            HandleUserCommand(words, "%", 0, 100, ref _battery);
                            break;
                        case "height":
                            HandleUserCommand(words, "cm", 0, 500, ref _height);
                            break;
                        case "yaw":
                            HandleUserCommand(words, "°", -180, 179, ref _yaw);
                            break;
                        case "pitch":
                            HandleUserCommand(words, "°", -90, 90, ref _pitch);
                            break;
                        case "roll":
                            HandleUserCommand(words, "°", -90, 90, ref _roll);
                            break;
                        case "wifi":
                            HandleUserCommand(words, "dBm", -99, 0, ref _wifiSnr);
                            break;
                        case "templ":
                            HandleUserCommand(words, "°C", 10, _tempHigh, ref _tempLow);
                            break;
                        case "temph":
                            HandleUserCommand(words, "°C", _tempLow, 100, ref _tempHigh);
                            break;
                        default:
                            Console.WriteLine("Unsupported command.");
                            break;
                    }
                }

                Console.WriteLine("Terminating - Please wait...");
                socket.Close();
                if (!receiver.Join(10000))
                    receiver.Abort();
            }
            catch (SocketException sex)
            {
                Console.WriteLine("FAILED");
                if (sex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    Console.Error.WriteLine($"ERROR: Another process is already bound to UDP port {localEndPoint.Port}\n" +
                                            "Is there another instance of the emulator running?");
                else
                    Console.Error.WriteLine(sex);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED");
                Console.Error.WriteLine("ERROR: " + ex);
            }
            finally
            {
                _readyWaitHandle?.Dispose();
            }

            Console.WriteLine("Goodbye.");
        }
    }
}
