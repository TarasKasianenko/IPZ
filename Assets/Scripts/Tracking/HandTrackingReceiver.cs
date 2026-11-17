using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace IPZ.Tracking
{
    [Serializable]
    class HandPacket
    {
        public long t;
        public HandData[] hands;
        public CameraState cameras;
    }

    [Serializable]
    public class CameraState
    {
        public int xy;
        public int z = -1;
        public int[] available;
        public bool scanning;
        public string error;
    }

    [Serializable]
    class HandData
    {
        public int player;
        public string side;
        public bool present;
        public float[] points;
        public float depth = -1f;
    }

    public class HandTrackingReceiver : MonoBehaviour
    {
        public const int LandmarkCount = 21;
        public const int PlayerCount = 2;
        public const int HandsPerPlayer = 2;
        public const int HandCount = PlayerCount * HandsPerPlayer;

        [SerializeField] int port = 5052;
        [SerializeField] float staleAfterSeconds = 0.5f;

        readonly object sync = new object();
        readonly Stopwatch clock = Stopwatch.StartNew();
        HandPacket latest;
        double latestAt;
        UdpClient commands;
        UdpClient client;
        Thread thread;
        volatile bool running;

        public static int PlayerOf(int hand) => hand / HandsPerPlayer;
        public static bool IsLeft(int hand) => hand % HandsPerPlayer == 0;

        public bool IsReceiving
        {
            get
            {
                lock (sync)
                    return latest != null && clock.Elapsed.TotalSeconds - latestAt < staleAfterSeconds;
            }
        }

        public bool TryGetHand(int hand, Vector3[] result)
        {
            if (result == null || result.Length < LandmarkCount)
                throw new ArgumentException($"result must hold {LandmarkCount} points");

            lock (sync)
            {
                if (latest?.hands == null || hand < 0 || hand >= latest.hands.Length
                    || clock.Elapsed.TotalSeconds - latestAt >= staleAfterSeconds)
                    return false;

                var h = latest.hands[hand];
                if (!h.present || h.points == null || h.points.Length < LandmarkCount * 3)
                    return false;

                for (int i = 0; i < LandmarkCount; i++)
                    result[i] = new Vector3(h.points[i * 3], 1f - h.points[i * 3 + 1], h.points[i * 3 + 2]);
                return true;
            }
        }

        public bool TryGetDepth(int hand, out float depth)
        {
            depth = -1f;
            lock (sync)
            {
                if (latest?.hands == null || latest.cameras == null || latest.cameras.z < 0
                    || hand < 0 || hand >= latest.hands.Length
                    || clock.Elapsed.TotalSeconds - latestAt >= staleAfterSeconds)
                    return false;

                var h = latest.hands[hand];
                if (!h.present || h.depth < 0f)
                    return false;
                depth = h.depth;
                return true;
            }
        }

        public CameraState Cameras
        {
            get
            {
                lock (sync)
                    return IsReceivingUnlocked() ? latest.cameras : null;
            }
        }

        public void SelectCameras(int xy, int z) => SendCommand($"{{\"xy\":{xy},\"z\":{z}}}");

        public void ScanCameras() => SendCommand("{\"scan\":true}");

        void SendCommand(string json)
        {
            try
            {
                commands ??= new UdpClient();
                var bytes = Encoding.UTF8.GetBytes(json);
                commands.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port + 3));
            }
            catch (SocketException e)
            {
                UnityEngine.Debug.LogWarning($"HandTrackingReceiver: {e.Message}");
            }
        }

        bool IsReceivingUnlocked() => latest != null && clock.Elapsed.TotalSeconds - latestAt < staleAfterSeconds;

        void OnEnable()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            running = true;
            thread = new Thread(Listen) { IsBackground = true, Name = "HandTrackingReceiver" };
            thread.Start();
        }

        void OnDisable()
        {
            running = false;
            client?.Close();
            thread?.Join(500);
            client = null;
            thread = null;
            commands?.Close();
            commands = null;
        }

        void Listen()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var bytes = client.Receive(ref remote);
                    var packet = JsonUtility.FromJson<HandPacket>(Encoding.UTF8.GetString(bytes));
                    lock (sync)
                    {
                        latest = packet;
                        latestAt = clock.Elapsed.TotalSeconds;
                    }
                }
                catch (SocketException) when (!running) { }
                catch (ObjectDisposedException) { }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"HandTrackingReceiver: {e.Message}");
                }
            }
        }
    }
}
