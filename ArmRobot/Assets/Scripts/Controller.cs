using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class Controller : MonoBehaviour
{
    public string portName = "COM3";
    public int baud = 115200;
    public int readTimeoutMs = 200;

    SerialPort sp;
    Thread reader;
    volatile bool running;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

    void Start()
    {
        Debug.Log("Ports: " + string.Join(", ", SerialPort.GetPortNames()));

        sp = new SerialPort(portName, baud)
        {
            NewLine = "\n",
            ReadTimeout = readTimeoutMs,
            DtrEnable = true,      // helpful for many USB CDC adapters
            RtsEnable = true,
            Encoding = Encoding.ASCII
        };

        try
        {
            sp.Open();
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true };
            reader.Start();
            Debug.Log($"Opened {sp.PortName} @ {baud}");
        }
        catch (Exception ex)
        {
            Debug.LogError("Open failed: " + ex.Message);
        }
    }

    void ReadLoop()
    {
        while (running && sp != null && sp.IsOpen)
        {
            try
            {
                string line = sp.ReadLine();          // blocks until '\n' or timeout
                if (!string.IsNullOrWhiteSpace(line))
                    inbox.Enqueue(line.Trim());
            }
            catch (TimeoutException) { /* ignore */ }
            catch (InvalidOperationException) { break; }     // port closed
            catch (System.IO.IOException) { /* transient; continue */ }
        }
    }

    void Update()
    {
        // Drain a few messages per frame to avoid log spam
        int n = 0;
        while (n++ < 5 && inbox.TryDequeue(out var msg))
        {
            // TODO: parse CSV into floats and drive your knife here
            // var parts = msg.Split(',');
            // float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
            Debug.Log(msg);
        }
    }

    void OnDisable() => ClosePort();
    void OnApplicationQuit() => ClosePort();

    void ClosePort()
    {
        running = false;
        try { reader?.Join(300); } catch { }
        try { if (sp?.IsOpen == true) sp.Close(); } catch { }
        try { sp?.Dispose(); } catch { }
        reader = null; sp = null;
    }
}
