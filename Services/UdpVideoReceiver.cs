using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GUI_12_19.Services;

public class UdpVideoReceiver
{
    private UdpClient? _udpClient;
    private bool _isRunning;
    private readonly int _port;
    private readonly List<byte> _frameBuffer = new List<byte>();
    
    // 画像を受信したときにViewModelへ通知するアクション
    public Action<Bitmap>? OnFrameReady;

    public UdpVideoReceiver(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        Task.Run(async () =>
        {
            try
            {
                // バッファサイズを大きく確保 (Python側の 4MB に合わせる)
                _udpClient = new UdpClient(_port);
                _udpClient.Client.ReceiveBufferSize = 4 * 1024 * 1024; 

                while (_isRunning)
                {
                    var result = await _udpClient.ReceiveAsync();
                    var data = result.Buffer;

                    if (data.Length < 2) continue;

                    // Pythonコード: flag = data[0], payload = data[1:]
                    byte flag = data[0];
                    
                    // ペイロード部分をバッファに追加
                    // (Skip(1)などは遅いのでBlockCopyやループを使うか、シンプルにAdd)
                    for (int i = 1; i < data.Length; i++)
                    {
                        _frameBuffer.Add(data[i]);
                    }

                    // flag == 1 はフレーム終了 (Pythonコード準拠)
                    if (flag == 1)
                    {
                        byte[] jpegBytes = _frameBuffer.ToArray();
                        _frameBuffer.Clear();

                        // JPEGバイト列をBitmapに変換 (UIスレッドへの通知は呼び出し元で行うか、ここで行う)
                        try 
                        {
                            using (var ms = new MemoryStream(jpegBytes))
                            {
                                var bitmap = new Bitmap(ms);
                                // メインスレッドでイベント発火
                                Dispatcher.UIThread.Post(() => OnFrameReady?.Invoke(bitmap));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Decode Error on Port {_port}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP Receiver Error ({_port}): {ex.Message}");
            }
            finally
            {
                _udpClient?.Close();
            }
        });
    }

    public void Stop()
    {
        _isRunning = false;
        _udpClient?.Close();
        _udpClient?.Dispose();
    }
}
