using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace IPZ.AirHockey
{
    /// Shows the JPEG preview the tracker sends, with the hand skeleton already drawn on it.
    /// The tracker owns the webcam, so Unity cannot open the camera itself.
    public class CameraThumbnail : MonoBehaviour
    {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField] int port = 5053;
        [SerializeField] Renderer screen;

        readonly object sync = new object();
        byte[] latest;
        UdpClient client;
        Thread thread;
        volatile bool running;
        Texture2D texture;
        MaterialPropertyBlock block;

        void Awake()
        {
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            running = true;
            thread = new Thread(Listen) { IsBackground = true, Name = "CameraThumbnail" };
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

        void OnDestroy() => Destroy(texture);

        void Update()
        {
            byte[] frame;
            lock (sync)
            {
                frame = latest;
                latest = null;
            }
            if (frame == null || !texture.LoadImage(frame))
                return;

            block.SetTexture(BaseMap, texture);
            screen.SetPropertyBlock(block);
        }

        void Listen()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var bytes = client.Receive(ref remote);
                    lock (sync)
                        latest = bytes;
                }
                catch (SocketException) when (!running) { }
                catch (ObjectDisposedException) { }
                catch (SocketException e)
                {
                    Debug.LogWarning($"CameraThumbnail: {e.Message}");
                }
            }
        }
    }
}
