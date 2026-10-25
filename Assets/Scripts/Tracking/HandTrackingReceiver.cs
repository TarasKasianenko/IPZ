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
        public HandPlayer[] players;
    }

    [Serializable]
    class HandPlayer
    {
        public int id;
        public bool present;
        public float[] points;
    }

    public class HandTrackingReceiver : MonoBehaviour
    {
        public const int LandmarkCount = 21;
        public const int PlayerCount = 2;

        [SerializeField] int port = 5052;
        [SerializeField] float staleAfterSeconds = 0.5f;

        readonly object sync = new object();
        readonly Stopwatch clock = Stopwatch.StartNew();
        HandPacket latest;
        double latestAt;
        UdpClient client;
        Thread thread;
        volatile bool running;

        public bool IsReceiving
        {
            get
            {
                lock (sync)
                    return latest != null && clock.Elapsed.TotalSeconds - latestAt < staleAfterSeconds;
            }
        }

        public bool TryGetHand(int player, Vector3[] result)
        {
            if (result == null || result.Length < LandmarkCount)
                throw new ArgumentException($"result must hold {LandmarkCount} points");

            lock (sync)
            {
                if (latest?.players == null || clock.Elapsed.TotalSeconds - latestAt >= staleAfterSeconds)
                    return false;

                foreach (var p in latest.players)
                {
                    if (p.id != player || !p.present || p.points == null || p.points.Length < LandmarkCount * 3)
                        continue;

                    for (int i = 0; i < LandmarkCount; i++)
                        result[i] = new Vector3(p.points[i * 3], 1f - p.points[i * 3 + 1], p.points[i * 3 + 2]);
                    return true;
                }
            }
            return false;
        }

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
